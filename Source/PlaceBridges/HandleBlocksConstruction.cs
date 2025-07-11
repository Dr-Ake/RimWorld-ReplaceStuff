﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Replace_Stuff.PlaceBridges
{
	[HarmonyPatch(typeof(GenConstruct), "BlocksConstruction")]
	class HandleBlocksConstruction
	{
		//public static bool BlocksConstruction(Thing constructible, Thing t)
		public static bool Prefix(ref bool __result, Thing constructible, Thing t)
		{
			if (t.def.entityDefToBuild.IsBridgelike())
			{
				//Bridges block non-bridges
				__result = !constructible.def.entityDefToBuild.IsBridgelike();
				return false;
			}
			return true;
		}
	}
	//No need to handle blocking job, that's just a construction job
}
