﻿using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace ConvImgCpc {
	public partial class SaveAnim : Form {
		private static byte[] BufPrec = new byte[0x8000];
		private static byte[] DiffImage = new byte[0x8000];
		private static byte[] BufTmp = new byte[0x8000];
		private static byte[] bLigne = new byte[0x8000];
		private static byte[] OldImgAscii = new byte[0x800];
		private char lastAscii = '\0';

		private string fileName;
		private string version;
		private ImageCpc img;
		private Param param;

		public SaveAnim(string f, string v, ImageCpc i, Param p) {
			fileName = f;
			version = v;
			img = i;
			param = p;
			InitializeComponent();
			grpGenereLigne.Visible = chk2Zone.Visible = chkDirecMem.Visible = chkCol.Visible = img.modeVirtuel < 7;
			grpAscii.Visible = img.modeVirtuel >= 7;
		}

		#region Méthodes de compactage
		private int PackWinDC(byte[] bufOut, ref int sizeDepack, int topBottom, bool razDiff, int modeLigne, bool optimSpeed) {
			int xFin = 0;
			int xDeb = img.NbCol;
			int yDeb = img.NbLig;
			int yFin = 0;
			int lStart = 0, lEnd = img.NbLig, xStart = 0, xEnd = img.NbCol;

			if (razDiff)
				Array.Clear(BufPrec, 0, BufPrec.Length);

			// Copier l'image cpc dans le buffer de travail
			img.bitmapCpc.CreeBmpCpc(img.BmpLock);

			if (chkZoneVert.Checked) {
				xStart = topBottom < 1 ? 0 : img.NbCol >> 1;
				xEnd = topBottom == 0 ? img.NbCol >> 1 : img.NbCol;
			}
			else {
				lStart = topBottom < 1 ? 0 : img.NbLig >> 1;
				lEnd = topBottom == 0 ? img.NbLig >> 1 : img.NbLig;
			}
			// Recherche les "coordonnées" de l'image différente par rapport à la précédente
			for (int l = lStart; l < lEnd; l += modeLigne) {
				int adr = img.GetAdrCpc(l << 1);
				for (int oct = xStart; oct < xEnd; oct++) {
					if (img.bitmapCpc.bmpCpc[adr + oct] != BufPrec[adr + oct]) {
						xDeb = Math.Min(xDeb, oct);
						xFin = Math.Max(xFin, oct);
						yDeb = Math.Min(yDeb, l);
						yFin = Math.Max(yFin, l);
						BufPrec[adr + oct] = img.bitmapCpc.bmpCpc[adr + oct];
					}
				}
			}

			int tailleX = xFin > xDeb ? xFin - xDeb + 1 : 0;
			int tailleY = (yFin + 1 - yDeb) / modeLigne;
			int length = tailleX * tailleY;
			if (length > 0) {
				Array.Clear(bLigne, 0, bLigne.Length);
				int pos = 0, AdrEcr;
				bLigne[pos++] = (byte)tailleX;
				bLigne[pos++] = (byte)tailleY;
				if (!optimSpeed) {
					AdrEcr = 0xC000 + xDeb + (yDeb >> 3) * img.NbCol + (yDeb & 7) * 0x800;
					bLigne[pos++] = (byte)(AdrEcr & 0xFF);
					bLigne[pos++] = (byte)(AdrEcr >> 8);
				}
				if (chkCol.Checked) {
					// passage en mode "colonne par colonne"
					for (int x = xDeb; x <= xFin; x++) {
						for (int l = 0; l < tailleY * modeLigne; l += modeLigne) {
							int offsetEcr = ((l + yDeb) >> 3) * img.NbCol + ((l + yDeb) & 7) * 0x800 + x;
							if (optimSpeed) {
								AdrEcr = 0xC000 + offsetEcr;
								bLigne[pos++] = (byte)(AdrEcr & 0xFF);
								bLigne[pos++] = (byte)(AdrEcr >> 8);
							}
							bLigne[pos++] = BufPrec[offsetEcr];
						}
					}
				}
				else {
					// Passage en mode "ligne à ligne"
					for (int l = yDeb; l <= yFin; l += modeLigne) {
						int offsetEcr = (l >> 3) * img.NbCol + (l & 7) * 0x800 + xDeb;
						if (optimSpeed) {
							AdrEcr = 0xC000 + offsetEcr;
							bLigne[pos++] = (byte)(AdrEcr & 0xFF);
							bLigne[pos++] = (byte)(AdrEcr >> 8);
						}
						Array.Copy(BufPrec, offsetEcr, bLigne, pos, tailleX);
						pos += tailleX;
					}
				}
				int lpack = PackDepack.Pack(bLigne, pos, bufOut, 0);
				sizeDepack = length + 4;
				return lpack;
			}
			else
				return 0;
		}

		private int PackDirectMem(byte[] bufOut, ref int sizeDepack, bool newMethode, bool razDiff) {
			if (razDiff)
				Array.Clear(BufPrec, 0, BufPrec.Length);

			// Copier l'image cpc dans le buffer de travail
			img.bitmapCpc.CreeBmpCpc(img.BmpLock);
			byte[] src = img.bitmapCpc.bmpCpc;

			int maxSize = (img.NbCol) + ((img.NbLig - 1) >> 3) * (img.NbCol) + ((img.NbLig - 1) & 7) * 0x800;
			if (maxSize >= 0x4000)
				maxSize += 0x3800;

			int maxDelta = newMethode ? 127 : 255;
			// Recherche les "coordonnées" de l'image différente par rapport à la précédente
			int bc = 0, posDiff = 0;
			byte deltaAdr = 0;
			for (int adr = 0; adr < maxSize; adr++) {
				byte o = BufPrec[adr];
				byte n = src[adr];
				if (deltaAdr == 127 || (o != n)) {
					if (newMethode && adr < maxSize - 256 && BufPrec[adr + 1] != src[adr + 1] && BufPrec[adr + 2] != src[adr + 2] && src[adr + 1] == n && src[adr + 2] == n) {
						DiffImage[posDiff++] = (byte)(deltaAdr | 0x80);
						bc++;
						int d = 0;
						while (d < 255 && src[adr + d] == n)
							d++;

						DiffImage[posDiff++] = (byte)d;
						DiffImage[posDiff++] = n;
						deltaAdr = 0;
						adr += d - 1;
					}
					else {
						DiffImage[posDiff++] = (byte)deltaAdr;
						DiffImage[posDiff++] = n;
						bc++;
						deltaAdr = 0;
						if (posDiff >= DiffImage.Length)
							break;
					}
				}
				else
					deltaAdr++;
			}
			BufTmp[0] = (byte)(bc);
			BufTmp[1] = (byte)(bc >> 8);
			Buffer.BlockCopy(DiffImage, 0, BufTmp, 2, posDiff);
			int lPack = PackDepack.Pack(BufTmp, posDiff + 2, bufOut, 0);
			Array.Copy(src, BufPrec, BufPrec.Length);
			sizeDepack = posDiff + 2;
			return lPack;
		}
		/*
				private int PackWinDC4(byte[] bufOut, bool razDiff = false) {
					int[] xFin = { 0, 0, 0, 0 };
					int[] xDeb = { img.NbCol, img.NbCol, img.NbCol, img.NbCol };
					int[] yDeb = { img.NbLig, img.NbLig, img.NbLig, img.NbLig };
					int[] yFin = { 0, 0, 0, 0 };
					int[] ld = { 0, 0, img.NbLig >> 1, img.NbLig >> 1 };
					int[] lf = { img.NbLig >> 1, img.NbLig >> 1, img.NbLig, img.NbLig };
					int[] od = { 0, img.NbCol >> 1, 0, img.NbCol >> 1 };
					int[] of = { img.NbCol >> 1, img.NbCol, img.NbCol >> 1, img.NbCol };
					if (razDiff)
						Array.Clear(BufPrec, 0, BufPrec.Length);

					// Copier l'image cpc dans le buffer de travail
					img.bitmapCpc.CreeBmpCpc(img.BmpLock);

					int posBufOut = 1;
					byte nbZone = 0;
					// Recherche les "coordonnées" de l'image différente par rapport à la précédente
					for (int i = 0; i < 4; i++) {
						for (int l = ld[i]; l < lf[i]; l++) {
							int adr = img.GetAdrCpc(l << 1);
							for (int oct = od[i]; oct < of[i]; oct++) {
								if (img.bitmapCpc.bmpCpc[adr + oct] != BufPrec[adr + oct]) {
									xDeb[i] = Math.Min(xDeb[i], oct);
									xFin[i] = Math.Max(xFin[i], oct);
									yDeb[i] = Math.Min(yDeb[i], l);
									yFin[i] = Math.Max(yFin[i], l);
									BufPrec[adr + oct] = img.bitmapCpc.bmpCpc[adr + oct];
								}
							}
						}
						if (xFin[i] >= xDeb[i] && yFin[i] >= yDeb[i]) {
							int nbOctets = xFin[i] - xDeb[i] + 1;
							int length = nbOctets * (yFin[i] + 1 - yDeb[i]);
							Array.Clear(bLigne, 0, bLigne.Length);
							int AdrEcr = xDeb[i] + (yDeb[i] >> 3) * img.NbCol + (yDeb[i] & 7) * 0x800;
							bLigne[0] = (byte)AdrEcr;
							bLigne[1] = (byte)(AdrEcr >> 8);
							bLigne[2] = (byte)(xFin[i] - xDeb[i] + 1);
							bLigne[3] = (byte)(yFin[i] - yDeb[i] + 1);
							// Passage en mode "ligne à ligne
							int pos = 4;
							for (int l = yDeb[i]; l <= yFin[i]; l++) {
								Array.Copy(BufPrec, (l >> 3) * img.NbCol + (l & 7) * 0x800 + xDeb[i], bLigne, pos, nbOctets);
								pos += nbOctets;
							}
							posBufOut = PackDepack.Pack(bLigne, length, bufOut, posBufOut);
							nbZone++;
						}
					}
					bufOut[0] = nbZone;
					return posBufOut;
				}
		*/
		private int PackAscii(byte[] bufOut, ref int sizeDepack, bool razDiff, bool firstFrame, bool imageMode, bool perte = false) {
			int posDiff = 0, lastPosDiff = 0, nDiff = 0;
			byte nbModif = 0;
			int tailleMax = (img.NbLig * img.NbCol) >> 3;

			if (razDiff)
				Array.Clear(OldImgAscii, 0, OldImgAscii.Length);

			if (perte) {
				for (int i = img.NbCol; i < tailleMax - img.NbCol; i++)
					if (OldImgAscii[i - 1] == img.bitmapCpc.imgAscii[i - 1] && OldImgAscii[i + 1] == img.bitmapCpc.imgAscii[i + 1]
						&& OldImgAscii[i - img.NbCol] == img.bitmapCpc.imgAscii[i - img.NbCol] && OldImgAscii[i + img.NbCol] == img.bitmapCpc.imgAscii[i + img.NbCol])
						img.bitmapCpc.imgAscii[i] = OldImgAscii[i];
			}
			for (int i = 0; i < tailleMax; i++) {
				byte oldAsc = OldImgAscii[i];
				byte newAsc = img.bitmapCpc.imgAscii[i];
				if (nbModif == 255 || (oldAsc != newAsc) || firstFrame) {
					if (oldAsc != newAsc) {
						nDiff++;
						lastPosDiff = (posDiff + 2);
					}
					DiffImage[posDiff++] = (byte)nbModif;
					DiffImage[posDiff++] = newAsc;
					nbModif = 0;
					if (posDiff >= DiffImage.Length)
						break;
				}
				else
					nbModif++;
			}
			Array.Copy(img.bitmapCpc.imgAscii, OldImgAscii, OldImgAscii.Length);
			sizeDepack = img.bitmapCpc.imgAscii.Length + 4;
			if (nDiff == 0) {
				BufTmp[0] = (byte)'I';
				lastAscii = 'I';
				return PackDepack.Pack(BufTmp, 1, bufOut, 0);
			}
			else {
				BufTmp[0] = (byte)'O';
				Array.Copy(img.bitmapCpc.imgAscii, 0, BufTmp, 1, tailleMax);
				int lo = rbFrameO.Checked || imageMode ? PackDepack.Pack(img.bitmapCpc.imgAscii, tailleMax, BufPrec, 0) : PackDepack.Pack(BufTmp, tailleMax + 1, BufPrec, 0);
				posDiff = Math.Min(lastPosDiff, posDiff);
				if (rbFrameD.Checked || imageMode) {
					BufTmp[0] = (byte)(posDiff >> 1);
					BufTmp[1] = (byte)(posDiff >> 9);
					Array.Copy(DiffImage, 0, BufTmp, 2, posDiff);
				}
				else {
					BufTmp[0] = (byte)'D';
					BufTmp[1] = (byte)(posDiff >> 1);
					BufTmp[2] = (byte)(posDiff >> 9);
					Array.Copy(DiffImage, 0, BufTmp, 3, posDiff);
				}
				int ld = PackDepack.Pack(BufTmp, posDiff + (rbFrameD.Checked ? 2 : 3), bufOut, 0);
				if (lo > ld && !rbFrameO.Checked || rbFrameD.Checked) {
					if (imageMode) {
						rbFrameFull.Checked = false;
						rbFrameD.Checked = true;
					}
					lastAscii = 'D';
					return ld;
				}
				else {
					Array.Copy(BufPrec, bufOut, lo);
					if (imageMode) {
						rbFrameFull.Checked = false;
						rbFrameO.Checked = true;
					}
					lastAscii = 'O';
					return lo;
				}
			}
		}

		private int PackFrame(byte[] bufOut, ref int sizeDepack, bool razDiff, bool firstFrame, int topBottom, int modeLigne, bool imageMode, bool optimSpeed) {
			if (img.modeVirtuel >= 7)
				return PackAscii(bufOut, ref sizeDepack, razDiff, firstFrame, imageMode);
			else
				if (chkDirecMem.Checked)
					return PackDirectMem(bufOut, ref sizeDepack, true, razDiff);
				else
					return PackWinDC(bufOut, ref sizeDepack, topBottom, razDiff, modeLigne, optimSpeed);
		}
		#endregion

		private void SauveDeltaPack(int adrDeb, int adrMax, int delai, int modeLigne, bool imageMode, bool optimSpeed) {
			int sizeDepack = 0;
			int nbImages = img.main.GetMaxImages();
			byte[][] bufOut = new byte[nbImages << 1][];
			int[] lg = new int[nbImages << 1];
			int[] bank = new int[nbImages << 1];
			for (int i = 0; i < nbImages << 1; i++)
				bufOut[i] = new byte[0x8000];

			if (adrMax == 0)
				adrMax = 0xBE00;

			if (chkBoucle.Checked) {
				img.main.SelectImage(nbImages - 1, true);
				img.Convert(true, true);
				PackFrame(bufOut[0], ref sizeDepack, true, false, -1, modeLigne, imageMode, optimSpeed);
			}

			img.main.SetInfo("Début sauvegarde animation assembleur...");
			// Calcule les animations
			int ltot = 0, maxDepack = 0;
			int posPack = 0;
			for (int i = 0; i < (imageMode ? 1 : nbImages); i++) {
				if (!imageMode) {
					img.main.SelectImage(i, true);
					img.Convert(true, true);
				}
				Application.DoEvents();
				if (chk2Zone.Checked) {
					lg[posPack] = PackFrame(bufOut[posPack], ref sizeDepack, i == 0 && !chkBoucle.Checked, i == 0 && !chkBoucle.Checked, 0, modeLigne, imageMode, optimSpeed);
					if (lg[posPack] > 0)
						ltot += lg[posPack++];

					lg[posPack] = PackFrame(bufOut[posPack], ref sizeDepack, i == 0 && !chkBoucle.Checked, i == 0 && !chkBoucle.Checked, 1, modeLigne, imageMode, optimSpeed);
					if (lg[posPack] > 0)
						ltot += lg[posPack++];
				}
				else {
					lg[posPack] = PackFrame(bufOut[posPack], ref sizeDepack, i == 0 && !chkBoucle.Checked, i == 0 && !chkBoucle.Checked, -1, modeLigne, imageMode, optimSpeed);
					if (lg[posPack] > 0)
						ltot += lg[posPack++];
				}
				maxDepack = Math.Max(maxDepack, sizeDepack);
			}

			// Sauvegarde
			StreamWriter sw = SaveAsm.OpenAsm(fileName, version, param);
			if (param.withCode) {
				SaveAsm.GenereEntete(sw, adrDeb, img.NbCol, img.NbLig);
				if (img.cpcPlus)
					SaveAsm.GenereInitPlus(sw);
				else
					SaveAsm.GenereInitOld(sw);
			}
			bool gest128K = chk128Ko.Checked;
			if ((ltot + adrDeb < adrMax) && (ltot + adrDeb < 0xBE00 - maxDepack))
				gest128K = false;

			if (param.withCode) {
				SaveAsm.GenereAffichage(sw, delai, img.modeVirtuel, gest128K, imageMode);
				if (img.modeVirtuel >= 7)
					SaveAsm.GenereDrawAscii(sw, img.modeVirtuel, rbFrameFull.Checked, rbFrameO.Checked, rbFrameD.Checked, gest128K, imageMode);
				else
					if (chkDirecMem.Checked)
						SaveAsm.GenereDrawDirect(sw, gest128K);
					else
						SaveAsm.GenereDrawDC(sw, delai, img.NbCol, chkCol.Checked, gest128K, modeLigne == 8 ? 0x3F : modeLigne == 4 ? 0x1F : modeLigne == 2 ? 0xF : 0x7, optimSpeed);
			}
			if (param.withPalette || param.withCode) {
				if (img.cpcPlus)
					SaveAsm.GenerePalettePlus(sw, img);
				else
					SaveAsm.GenerePaletteOld(sw, img);
			}
			int endBank0 = 0;
			int lbank = 0, numBank = 0xC0;
			for (int i = 0; i < posPack; i++) {
				lbank += lg[i];
				if (gest128K && lbank > (numBank == 0xC0 ? Math.Min((0xBE00 - maxDepack - adrDeb), adrMax - adrDeb) : 0x4000) && (numBank > 0xC0 || lbank + adrDeb - lg[i] > 0x4000)) {
					if (numBank == 0xC0) {
						endBank0 = lbank + adrDeb - lg[i];
						sw.WriteLine("EndBank0:");
						numBank = 0xC4;
					}
					else {
						numBank++;
						if ((numBank & 15) == 8)
							numBank += 4;

						if ((numBank & 15) == 15)
							numBank += 5;
					}
					lbank = lg[i];
					sw.WriteLine("	ORG	#4000");
					sw.WriteLine("	Write Direct -1,-1,#" + numBank.ToString("X2"));
				}
				bank[i] = numBank;
				if (imageMode && lastAscii != '\0')
					sw.WriteLine("; Type Frame ='" + lastAscii + "'");

				sw.WriteLine("Delta" + i.ToString() + ":\t\t; Taille #" + lg[i].ToString("X4"));
				SaveAsm.GenereDatas(sw, bufOut[i], lg[i], param);
			}
			if (gest128K)
				SaveAsm.GenerePointeurs(sw, img.NbCol, posPack, bank, gest128K && numBank > 0xC0);
			else
				if (!imageMode) {
					sw.WriteLine("	DB	#FF			; Fin de l'animation");
					ltot++;
				}
			SaveAsm.GenereFin(sw, img.modeVirtuel, ltot, gest128K && endBank0 < 0x8000);
			SaveAsm.CloseAsm(sw);
			for (int i = 0; i < posPack; i++)
				bufOut[i] = null;

			img.main.SetInfo("Longueur totale données animation : " + ltot + " octets.");
			if (numBank > 0xC7 || (!chk128Ko.Checked && ltot + adrDeb >= 0xBE00 - maxDepack)) {
				MessageBox.Show("Attention ! la taille totale (animation + buffer de décompactage) dépassera " + (chk128Ko.Checked ? "112K" : "48Ko") + ", risque d'écrasement de la mémoire vidéo et plantage..."
								, "Alerte"
								, MessageBoxButtons.OK
								, MessageBoxIcon.Warning);
				img.main.SetInfo("Dépassement capacité mémoire...");
			}
			else
				img.main.SetInfo("Sauvegarde animation assembleur ok.");

			GC.Collect();
		}

		private void chkMaxMem_CheckedChanged(object sender, EventArgs e) {
			tbxAdrMax.Enabled = chkMaxMem.Checked;
		}

		private void chk128Ko_CheckedChanged(object sender, EventArgs e) {
			tbxAdrMax.Visible = chkMaxMem.Visible = chk128Ko.Checked;
		}

		private void chkDelai_CheckedChanged(object sender, EventArgs e) {
			numDelai.Visible = lblDelai.Visible = chkDelai.Checked;
		}

		private void chkDirecMem_CheckedChanged(object sender, EventArgs e) {
			chk2Zone.Visible = chkZoneVert.Visible = grpGenereLigne.Visible = !chkDirecMem.Checked && img.modeVirtuel < 7;
			chkZoneVert.Visible = chk2Zone.Visible && chk2Zone.Checked;
		}

		private void chk2Zone_CheckedChanged(object sender, EventArgs e) {
			chkZoneVert.Visible = chk2Zone.Checked;
		}

		public void DoSave(bool imageMode) {
			string adrTxt = txbAdrDeb.Text;
			int adrDeb = 0, adrMax = 0;
			try {
				adrDeb = int.Parse(adrTxt.Substring(1), (adrTxt[0] == '#' || adrTxt[0] == '&') ? NumberStyles.HexNumber : NumberStyles.Integer);
			}
			catch (FormatException ex) {
				MessageBox.Show("L'adresse saisie [" + adrTxt + "] est erronée");
			}
			if (chkMaxMem.Checked) {
				adrTxt = tbxAdrMax.Text;
				try {
					adrMax = int.Parse(adrTxt.Substring(1), (adrTxt[0] == '#' || adrTxt[0] == '&') ? NumberStyles.HexNumber : NumberStyles.Integer);
				}
				catch (FormatException ex) {
					MessageBox.Show("L'adresse saisie [" + adrTxt + "] est erronée");
				}
			}
			bool optimSpeed = true;
			int modeLigne = rb8L.Checked ? 8 : rb4L.Checked ? 4 : rb2L.Checked ? 2 : 1;
			if (adrDeb > 0) {
				img.WindowState = FormWindowState.Minimized;
				img.Show();
				img.WindowState = FormWindowState.Normal;
				SauveDeltaPack(adrDeb, adrMax, chkDelai.Checked ? (int)numDelai.Value : 0, modeLigne, imageMode, optimSpeed);
			}
		}

		private void bpSave_Click(object sender, EventArgs e) {
			DoSave(false);
			Close();
		}
	}
}
