﻿using System;

namespace ConvImgCpc {
	[Serializable]
	public class Param {
		public enum SizeMode { Fit, KeepSmaller, KeepLarger };

		public SizeMode sizeMode;
		public string methode;
		public int pct;
		public int[] lockState = new int[16];
		public int pctLumi;
		public int pctSat;
		public int pctContrast;
		public bool cpcPlus;
		public bool newMethode;
		public bool reductPal1;
		public bool reductPal2;
		public bool newReduct;
		public bool sortPal;
		public int nbCols, nbLignes;
		public string modeCpc;
	}
}
