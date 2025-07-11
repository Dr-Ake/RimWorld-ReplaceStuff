using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Replace_Stuff.Utilities
{
	public static class RemoveThingFromStatWorkerCache
	{
		//class StatDef {
		//private StatWorker workerInt;
		public static AccessTools.FieldRef<StatDef, StatWorker> workerInt = AccessTools.FieldRefAccess<StatDef, StatWorker>("workerInt");

		//class StatWorker {
		//private Dictionary<Thing, StatCacheEntry> temporaryStatCache;
		//private Dictionary<Thing, float> immutableStatCache;
		public static AccessTools.FieldRef<StatWorker, Dictionary<Thing, StatCacheEntry>> temporaryStatCache = AccessTools.FieldRefAccess<StatWorker, Dictionary<Thing, StatCacheEntry>>("temporaryStatCache");
		public static AccessTools.FieldRef<StatWorker, ConcurrentDictionary<Thing, float>> immutableStatCache = AccessTools.FieldRefAccess<StatWorker, ConcurrentDictionary<Thing, float>>("immutableStatCache");

		public static void RemoveFromStatWorkerCaches(this Thing thing)
		{
			foreach (StatDef statDef in DefDatabase<StatDef>.AllDefsListForReading)
			{
				// Corrected line: Use the delegate to access the private field.
				var worker = workerInt(statDef);
				if (worker != null)
				{
					// Corrected lines: Use the delegates to access the private fields.
					temporaryStatCache(worker)?.Remove(thing);
					immutableStatCache(worker)?.TryRemove(thing, out _);
				}
			}
		}
	}
}
