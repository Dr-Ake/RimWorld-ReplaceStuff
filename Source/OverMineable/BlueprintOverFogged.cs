﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Replace_Stuff.OverMineable
{
	public static class FogUtil
	{
		public static bool IsUnderFog(this Thing thing)
		{
			return IsUnderFog(thing.Position, thing.Rotation, thing.def);
		}

		public static bool IsUnderFog(this IntVec3 center, Rot4 rot, ThingDef thingDef)
		{
			return GenAdj.OccupiedRect(center, rot, thingDef.Size)
			.Any(pos => Find.CurrentMap.fogGrid.IsFogged(pos));
		}
	}

	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt))]
	public static class BlueprintOverFogged
	{
		//public static AcceptanceReport CanPlaceBlueprintAt(BuildableDef entDef, IntVec3 center, Rot4 rot, Map map, bool godMode = false, Thing thingToIgnore = null)
		// ohheck this method has got a lot more
		//public static AcceptanceReport CanPlaceBlueprintAt(BuildableDef entDef, IntVec3 center, Rot4 rot, Map map, bool godMode = false, Thing thingToIgnore = null, Thing thing = null, ThingDef stuffDef = null, bool ignoreEdgeArea = false, bool ignoreInteractionSpots = false, bool ignoreClearableFreeBuildings = false)
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo FoggedInfo = AccessTools.Method(typeof(GridsUtility), "Fogged", new Type[] { typeof(IntVec3), typeof(Map) });

			MethodInfo BlueprintAcceptedInfo = AccessTools.Method(typeof(BlueprintOverFogged), nameof(BlueprintOverFogAcceptance));

			bool foundFogged = false;
			foreach (CodeInstruction i in instructions)
			{
				yield return i;
				if (foundFogged)  //skip the brfalse after Fogged
				{
					//This should probably check for DesignatorContext.designating but then more of this code would need to change
					yield return new CodeInstruction(OpCodes.Ldarg_3);//map
					yield return new CodeInstruction(OpCodes.Ldarg_1);//center
					yield return new CodeInstruction(OpCodes.Ldarg_0);//entDef
					yield return new CodeInstruction(OpCodes.Call, BlueprintAcceptedInfo);
					yield return new CodeInstruction(OpCodes.Ret);
					foundFogged = false;
				}
				if (i.Calls(FoggedInfo))
					foundFogged = true;
			}
		}

		//if found fogged:
		public static AcceptanceReport BlueprintOverFogAcceptance(Map map, IntVec3 center, ThingDef entDef)
		{
			if (!OverMineable.PlaySettings_BlueprintOverRockToggle.blueprintOverRock)
				return new AcceptanceReport("CannotPlaceInUndiscovered".Translate());
			if (center.GetThingList(map).Any(t => t is Blueprint && t.def.entityDefToBuild == entDef))
				return new AcceptanceReport("IdenticalBlueprintExists".Translate());
			if (entDef.GetStatValueAbstract(StatDefOf.WorkToBuild) == 0f)
				return new AcceptanceReport("CannotPlaceInUndiscovered".Translate());
			return true;
		}
	}


	// 1) Change Conduit PlaceWorker to allow conduits over conduits

	// 2) TL;DR: actually ignore the thingToIgnore argument
	//This patch fixes a vanilla bug/oversight, that forgot to check thingToIgnore in conduit's placeworker.
	//This wasn't a problem in vanilla, but with replace stuff, conduit blueprints would disappear when doors are opened
	//Since revealing new areas now triggers re-checking blueprints, power conduit blueprints would check their tile.
	//They'd find themselves, and determine they can't exist because there's already a power conduit there.
	//The thingToIgnore arguments already exists, to, you know, ignore that. But it wasn't checked.
	[HarmonyPatch(typeof(PlaceWorker_Conduit), "AllowsPlacing")]
	public static class FixConduitPlaceWorker
	{
		//AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null)
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			FieldInfo entityDefInfo = AccessTools.Field(typeof(ThingDef), "entityDefToBuild");

			//need to find loop continue label
			object continueLabel = null;

			//just easier to find the 3-line code to get thingList[i]
			bool foundLdLoc = false;
			List<CodeInstruction> currentThing = new List<CodeInstruction>();

			List<CodeInstruction> iList = instructions.ToList();
			for(int k=0; k < iList.Count(); k++)
			{
				CodeInstruction i = iList[k];
				if (!foundLdLoc && i.opcode == OpCodes.Ldloc_0)
				{
					foundLdLoc = true;
					currentThing.AddRange(iList.GetRange(k, 3));
				}
				if (i.LoadsField(entityDefInfo))
				{
					continueLabel = iList[k+1].operand;
					break;
				}
			}

			foundLdLoc = false;
			foreach(CodeInstruction i in instructions)
			{
				if(!foundLdLoc && i.opcode == OpCodes.Ldloc_0)
				{
					foundLdLoc = true;

					// At start of loop, insert:

					// Noop with start of loop label
					yield return new CodeInstruction(OpCodes.Nop) { labels = i.labels };//start of loop label
					i.labels = new List<Label>();


					// if(thingList[i] == thingToIgnore)
					//	continue;
					foreach (CodeInstruction ci in currentThing)
						yield return new CodeInstruction(ci.opcode, ci.operand);//thingList[i] (Thing)

					yield return new CodeInstruction(OpCodes.Ldarg_S, 5);//thingToIgnore
					yield return new CodeInstruction(OpCodes.Beq, continueLabel);//if( ... == ... ) continue;


					/* 1.6 replaceTags should cover this
					// if(thingList[i].def.building.isPowerConduit)
					//	continue;
					foreach (CodeInstruction ci in currentThing)
						yield return new CodeInstruction(ci.opcode, ci.operand);//thingList[i] (Thing)

					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FixConduitPlaceWorker), nameof(IsConduit))); // ... .IsConduit()
					yield return new CodeInstruction(OpCodes.Brtrue, continueLabel);// if(...) continue;
					*/
				}
				yield return i;
			}
		}

		public static bool IsConduit(Thing t)
		{
			return (t.def.building?.isPowerConduit ?? false);
		}
	}
	

	[HarmonyPatch(typeof(FogGrid), "UnfogWorker")]
	public static class UnFogFix
	{
		//private void UnfogWorker(IntVec3 c)
		public static void Postfix(FogGrid __instance, IntVec3 c, Map ___map)
		{
			Map map = ___map;
			if (c.GetThingList(map).FirstOrDefault(t => t.def.IsBlueprint) is Thing blueprint && !blueprint.IsUnderFog())
			{
				DesignatorContext.designating = true; // as good as designating.

				if (!GenConstruct.CanPlaceBlueprintAt(blueprint.def.entityDefToBuild, blueprint.Position, blueprint.Rotation, map, false, blueprint).Accepted)
					blueprint.Destroy();
				else
					blueprint.Notify_ColorChanged();//does the job, haha.

				DesignatorContext.designating = false;
			}
		}
	}
}
