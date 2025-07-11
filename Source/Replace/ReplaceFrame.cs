﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;
using HarmonyLib;
using Replace_Stuff.Utilities;
using Replace_Stuff.NewThing;

namespace Replace_Stuff
{
	class ReplaceFrame : Frame
	{
		public Thing oldThing;
		public ThingDef oldStuff;
		
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref oldThing, "oldThing");
			Scribe_Defs.Look(ref oldStuff, "oldStuff");
		}
		
		private const float MaxDeconstructWork = 3000f;
		public static float WorkToDeconstructDef(ThingDef def, ThingDef oldStuff = null)
		{
			float deWork = (def.entityDefToBuild as ThingDef ?? def)
				.GetStatValueAbstract(StatDefOf.WorkToBuild, oldStuff);
			return Mathf.Min(deWork, MaxDeconstructWork);
		}

		public float WorkToDeconstruct
		{
			get
			{
				return WorkToDeconstructDef(def.entityDefToBuild as ThingDef, oldStuff);
			}
		}

		public float WorkToReplace
		{
			get
			{
				return def.entityDefToBuild.GetStatValueAbstract(StatDefOf.WorkToBuild, Stuff);
			}
		}
		public new float WorkToBuild
		{
			get
			{
				return WorkToDeconstructDef(def, oldStuff) + WorkToReplace;
			}
		}

		public override string Label
		{
			get
			{
				string text = this.def.entityDefToBuild.label + "TD.ReplacingTag".Translate();
				if (base.Stuff != null)
				{
					return base.Stuff.label + " " + text;
				}
				return text;
			}
		}

		public int TotalStuffNeeded()
		{
			return TotalStuffNeeded(def.entityDefToBuild, Stuff);
		}
		public static int TotalStuffNeeded(BuildableDef toBuild, ThingDef stuff)
		{
			int count = Mathf.RoundToInt((float)toBuild.costStuffCount / stuff.VolumePerUnit);
			if (count < 1) count = 1;
			return count;
		}
		public int CountStuffHas()
		{
			return resourceContainer.TotalStackCountOfDef(Stuff);
		}
		public int CountStuffNeeded()
		{
			return TotalStuffNeeded() - CountStuffHas();
		}

		// This is the corrected implementation that avoids the inaccessible classes.
		public new List<ThingDefCountClass> TotalMaterialCost()
		{
			List<ThingDefCountClass> costList = new List<ThingDefCountClass>();
			int stuffNeeded = this.CountStuffNeeded();
			if (stuffNeeded > 0)
			{
				costList.Add(new ThingDefCountClass(this.Stuff, stuffNeeded));
			}
			return costList;
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
		}

		public new void CompleteConstruction(Pawn worker)
		{
			if (oldThing != null && oldThing.Spawned)
			{
				FinalizeReplace(oldThing, Stuff, worker);

				this.resourceContainer.ClearAndDestroyContents(DestroyMode.Vanish);
				this.Destroy(DestroyMode.Vanish);

				worker?.records.Increment(RecordDefOf.ThingsConstructed);
				worker?.records.Increment(RecordDefOf.ThingsDeconstructed);
			}
			else
			{
				this.resourceContainer.TryDropAll(Position, Map, ThingPlaceMode.Near);
				this.Destroy(DestroyMode.Cancel);
			}
		}

		public new void FailConstruction(Pawn worker)
		{
			Log.Message($"Failed replace frame! work was {workDone}, Decon is {WorkToDeconstructDef(def, oldStuff)}, total is {WorkToBuild}");

			workDone = Mathf.Min(workDone, WorkToDeconstructDef(def, oldStuff));
			if (workDone < WorkToDeconstructDef(def, oldStuff)) return;  //Deconstruction doesn't fail

			GenLeaving.DoLeavingsFor(this, Map, DestroyMode.FailConstruction);
			
			MoteMaker.ThrowText(this.DrawPos, Map, "TextMote_ConstructionFail".Translate());
			if (base.Faction == Faction.OfPlayer && this.WorkToReplace > 1400f)
			{
				Messages.Message("MessageConstructionFailed".Translate(this.LabelEntityToBuild, worker.LabelShort, worker.Named("WORKER")),
					new TargetInfo(base.Position, Map), MessageTypeDefOf.NegativeEvent);
			}
		}

		public delegate Func<int, int> GetBuildingResourcesLeaveCalculatorDel(Thing oldThing, DestroyMode mode);
		public static GetBuildingResourcesLeaveCalculatorDel GetBuildingResourcesLeaveCalculator =
			AccessTools.MethodDelegate<GetBuildingResourcesLeaveCalculatorDel>(AccessTools.Method(typeof(GenLeaving), "GetBuildingResourcesLeaveCalculator"));

		public static void DeconstructDropStuff(Thing oldThing)
		{
			if (Current.ProgramState != ProgramState.Playing)	return;

			ThingDef oldDef = oldThing.def;
			ThingDef stuffDef = oldThing.Stuff;

			//preferably GenLeaving.DoLeavingsFor here, but don't want to drop non-stuff things.
			if (GenLeaving.CanBuildingLeaveResources(oldThing, DestroyMode.Deconstruct))
			{
				int count = TotalStuffNeeded(oldDef, stuffDef);
				int leaveCount = GetBuildingResourcesLeaveCalculator(oldThing, DestroyMode.Deconstruct)(count);
				if (leaveCount > 0)
				{
					Thing leftThing = ThingMaker.MakeThing(stuffDef);
					leftThing.stackCount = leaveCount;
					GenDrop.TryDropSpawn(leftThing, oldThing.Position, oldThing.Map, ThingPlaceMode.Near, out Thing dummyThing);
				}
			}
		}

		public static void FinalizeReplace(Thing thing, ThingDef stuff, Pawn worker = null)
		{
			DeconstructDropStuff(thing);

			thing.SetStuffDirect(stuff);
			thing.RemoveFromStatWorkerCaches();
			thing.HitPoints = thing.MaxHitPoints; //Deconstruction/construction implicitly repairs
			thing.Notify_ColorChanged();

			if (worker != null && thing.TryGetComp<CompQuality>() is CompQuality compQuality)
			{
				QualityCategory qualityCreatedByPawn = QualityUtility.GenerateQualityCreatedByPawn(worker, SkillDefOf.Construction);
				compQuality.SetQuality(qualityCreatedByPawn, ArtGenerationContext.Colony);
				QualityUtility.SendCraftNotification(thing, worker);
			}
		}

		public override string GetInspectString()
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("ContainedResources".Translate() + ":");
			stringBuilder.AppendLine(string.Concat(new object[]
			{
				Stuff.LabelCap,
				": ",
				CountStuffHas(),
				" / ",
				TotalStuffNeeded()
			}));
			stringBuilder.Append("WorkLeft".Translate() + ": " + this.WorkLeft.ToStringWorkAmount());
			return stringBuilder.ToString();
		}
	}


	//VIRTUAL virtual methods
	[HarmonyPatch(typeof(Frame), nameof(Frame.TotalMaterialCost))]
	public static class Virtualize_TotalMaterialCost
	{
		//public List<ThingDefCountClass> TotalMaterialCost()
		public static bool Prefix(Frame __instance, ref List<ThingDefCountClass> __result)
		{
			if (__instance is ReplaceFrame replaceFrame)
			{
				__result = replaceFrame.TotalMaterialCost();
				return false;
			}
			return true;
		}
	}
	[HarmonyPatch(typeof(Frame), "CompleteConstruction")]
	public static class Virtualize_CompleteConstruction
	{
		//public void CompleteConstruction(Pawn worker)
		public static bool Prefix(Frame __instance, Pawn worker)
		{
			if (__instance is ReplaceFrame replaceFrame)
			{
				replaceFrame.CompleteConstruction(worker);
				return false;
			}
			return true;
		}
	}
	[HarmonyPatch(typeof(Frame), "FailConstruction")]
	public static class Virtualize_FailConstruction
	{
		//public void FailConstruction(Pawn worker)
		public static bool Prefix(Frame __instance, Pawn worker)
		{
			if (__instance is ReplaceFrame replaceFrame)
			{
				replaceFrame.FailConstruction(worker);
				return false;
			}
			return true;
		}
	}
	[HarmonyPatch(typeof(Frame), "WorkToBuild", MethodType.Getter)]
	public static class Virtualize_WorkToBuild
	{
		//public float WorkToBuild
		public static bool Prefix(Frame __instance, ref float __result)
		{
			if (__instance is ReplaceFrame replaceFrame)
			{
				__result = replaceFrame.WorkToBuild;
				return false;
			}
			return true;
		}
	}
}
