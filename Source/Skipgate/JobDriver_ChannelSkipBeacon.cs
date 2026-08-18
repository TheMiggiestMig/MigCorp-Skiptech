using MigCorp.Skiptech.Comps;
using MigCorp.Skiptech.Skipgate.Comps;
using MigCorp.Skiptech.Skipgate.Operations;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.Skipgate
{
    public class JobDriver_ChannelSkipBeacon : JobDriver
    {
        private CompSkipBeacon ClaimedBeacon
        {
            get
            {
                List<Apparel> worn = pawn.apparel?.WornApparel;
                if (worn == null) { return null; }

                for (int i = 0; i < worn.Count; i++)
                {
                    CompSkipBeacon comp = SkipBeaconUtil.BeaconComp(worn[i]);

                    if (comp != null && comp.IsClaimed) { return comp; }
                }

                return null;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(delegate (JobCondition condition)
            {
                ClaimedBeacon?.RecallingGateComp?.CurrentOperation?.TryCancel();
            });

            Toil channel = ToilMaker.MakeToil("ChannelSkipBeacon");
            channel.defaultCompleteMode = ToilCompleteMode.Never;
            channel.handlingFacing = false;

            yield return channel.WithProgressBar(TargetIndex.A, delegate
            {
                CompSkipgate claimingGate = ClaimedBeacon?.RecallingGateComp;
                SkipgateOperation operation = claimingGate?.CurrentOperation;

                if (operation == null || operation.RequiredCharge <= 0f) { return 0f; }

                return Mathf.Clamp01(claimingGate.Capacitor.Charge / operation.RequiredCharge);
            });
        }
    }
}