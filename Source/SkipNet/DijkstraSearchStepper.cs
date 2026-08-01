using System;
using System.Collections.Generic;
using Verse;

namespace MigCorp.Skiptech.SkipNet
{
    public class DijkstraSearchStepper
    {
        private TraverseParms tp;
        private float maxCost;
        private Action<Region, IntVec3, float> onRegionTouched;

        private struct RegionEntry
        {
            public Region region;
            public IntVec3 anchor;
            public float g;
        }

        private class EntryComparer : IComparer<RegionEntry>
        {
            public int Compare(RegionEntry a, RegionEntry b) => a.g.CompareTo(b.g);
        }
        private static readonly EntryComparer comparer = new EntryComparer();

        private readonly FastPriorityQueue<RegionEntry> frontier = new FastPriorityQueue<RegionEntry>(comparer);
        private readonly Dictionary<int, float> regionCost = new Dictionary<int, float>();
        private readonly HashSet<int> closedRegions = new HashSet<int>();

        // FastPriorityQueue doesn't let us peek the top value, so we'll just hold onto it instead.
        private RegionEntry next;
        private bool hasNext;
        public int PopCount { get; private set; }

        public float CheapestCost => hasNext ? next.g : float.PositiveInfinity;

        public void Reset()
        {
            frontier.Clear();
            regionCost.Clear();
            closedRegions.Clear();
            hasNext = false;
            PopCount = 0;
        }

        public void Initialize(Region startRegion, IntVec3 startCell, float maxCost,
                               TraverseParms tp,
                               Action<Region, IntVec3, float> onRegionTouched)
        {
            this.tp = tp;
            this.maxCost = maxCost;
            this.onRegionTouched = onRegionTouched;

            // Check the starting region.
            CloseRegion(startRegion, startCell, 0f, isSeed: true);
            PullNext();
        }

        public void StepOnce()
        {
            if (!hasNext) { return; }

            RegionEntry entry = next;
            hasNext = false;

            CloseRegion(entry.region, entry.anchor, entry.g);
            PullNext();
        }

        private void CloseRegion(Region region, IntVec3 anchor, float g, bool isSeed = false)
        {
            closedRegions.Add(region.id);
            PopCount++;

            if (!isSeed && !region.Allows(tp, isDestination: false)) { return; }

            onRegionTouched(region, anchor, g);

            // Add regions based on the Region's RegionLinks.
            List<RegionLink> links = region.links;
            for (int i = 0; i < links.Count; i++)
            {
                RegionLink link = links[i];
                Region other = link.GetOtherRegion(region);
                if (other == null || !other.valid || closedRegions.Contains(other.id)) { continue; }

                IntVec3 hopAnchor = LinkAnchor(link.span, anchor);
                float cost = g + SkipNetUtils.OctileDistance(anchor, hopAnchor);

                if (cost >= maxCost) { continue; }

                // If it's a fresh region, or we saw this region before and it's cheaper to get to it now,
                // add it and we'll evaluate.
                if (!regionCost.TryGetValue(other.id, out float known) || cost < known)
                {
                    regionCost[other.id] = cost;
                    frontier.Push(new RegionEntry { region = other, anchor = hopAnchor, g = cost });
                }
            }
        }

        // Since we can't "peek" FastPriorityQueues, it grabs our best entry while doubling as
        // a lazy way to pop stale entries off until we get a valid one.
        private void PullNext()
        {
            while (frontier.Count > 0)
            {
                RegionEntry entry = frontier.Pop();

                if (closedRegions.Contains(entry.region.id)) { continue; }
                if (regionCost.TryGetValue(entry.region.id, out float known) && known < entry.g) { continue; }

                next = entry;
                hasNext = true;
                return;
            }
        }

        /*
        // Figure out where the middle of a RegionLink is. Good enough as the link's "position".
        private static IntVec3 LinkAnchor(RegionLink link)
        {
            EdgeSpan span = link.span;
            return span.dir == SpanDirection.North
                ? new IntVec3(span.root.x, 0, span.root.z + span.length / 2)
                : new IntVec3(span.root.x + span.length / 2, 0, span.root.z);
        }
        */

        //Figure out where the closest point of a RegionLink is to the entry anchor of a region.
        private static IntVec3 LinkAnchor(EdgeSpan span, IntVec3 from)
        {
            if (span.dir == SpanDirection.North)
            {
                int z = from.z < span.root.z ? span.root.z
                      : from.z >= span.root.z + span.length ? span.root.z + span.length - 1
                      : from.z;
                return new IntVec3(span.root.x, 0, z);
            }
            else
            {
                int x = from.x < span.root.x ? span.root.x
                      : from.x >= span.root.x + span.length ? span.root.x + span.length - 1
                      : from.x;
                return new IntVec3(x, 0, span.root.z);
            }
        }

    }
}