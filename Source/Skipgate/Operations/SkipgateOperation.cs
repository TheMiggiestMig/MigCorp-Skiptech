using MigCorp.Skiptech.Skipgate.Comps;
using Verse;

namespace MigCorp.Skiptech.Skipgate.Operations
{
    public enum SkipgateOperationPhase
    {
        Preparing,
        Dialing,
        Active
    }

    public enum SkipgateOperationType
    {
        SendLoad,
        SendCaravan,
        Recall,
        Link
    }

    public enum SkipgateOperationEnd
    {
        Completed,
        Cancelled,
        Failed
    }

    public abstract class SkipgateOperation : IExposable
    {
        protected readonly CompSkipgate gate;

        protected SkipgateOperationPhase phase = SkipgateOperationPhase.Preparing;

        protected float requiredCharge;
        protected int dialingTicksLeft;

        // Prevent completion/cancellation being performed twice in one tick.
        private bool ending;

        public abstract SkipgateOperationType Type { get; }
        public SkipgateOperationPhase Phase => phase;
        public float RequiredCharge => requiredCharge;
        protected virtual bool ChecksCapacityPolicy => true;
        protected virtual float CancelHeat => 0f; // The heat applied on cancel. Will usually be 0f, but Unlinking changes that.
        protected virtual bool UsesCapacitor => true;
        protected virtual bool PreparationReady => true;
        protected bool ChargeReady => !UsesCapacitor || gate.Capacitor.Charge >= requiredCharge;
        protected bool ReadyToDial => ChargeReady && PreparationReady;

        protected SkipgateOperation(CompSkipgate gate)
        {
            this.gate = gate;
        }

        // Only call for newly created operations!
        // Save/Loaded ones try to use ResumeAfterLoad.
        public virtual void Start()
        {
            phase = SkipgateOperationPhase.Preparing;
            ApplyRuntimeState();
        }

        public virtual AcceptanceReport CanStart()
        {
            if (ChecksCapacityPolicy && !gate.Capacitor.IsWithinCapacity(requiredCharge))
            {
                return "Charge cost exceeds the capacitor's safe operating limit.";
            }

            return true;
        }

        public void Tick(int delta)
        {
            if (ending) { return; }

            switch (phase)
            {
                case SkipgateOperationPhase.Preparing:
                    TickPreparing(delta);
                    break;

                case SkipgateOperationPhase.Dialing:
                    TickDialing(delta);
                    break;

                case SkipgateOperationPhase.Active:
                    TickActive(delta);
                    break;
            }
        }

        protected virtual void TickPreparing(int delta)
        {
            if (ReadyToDial) { BeginDialing(gate.Props.dialingTicks); }
        }

        protected virtual void TickDialing(int delta)
        {
            if (UsesCapacitor && !gate.Capacitor.Powered)
            {
                dialingTicksLeft = gate.Props.dialingTicks;
                return;
            }

            dialingTicksLeft -= delta;

            if (dialingTicksLeft <= 0)
            {
                dialingTicksLeft = 0;
                Execute();
            }
        }

        protected virtual void TickActive(int delta) { }

        protected void BeginDialing(int duration)
        {
            phase = SkipgateOperationPhase.Dialing;
            dialingTicksLeft = duration;

            if (dialingTicksLeft <= 0) { Execute(); }
        }

        // Used by Link after its portals have opened. Other operations will normally call CompleteOperation instead.
        protected void EnterActive()
        {
            phase = SkipgateOperationPhase.Active;
            ApplyRuntimeState();
        }

        // Performs the actual teleport/open-link once dialing finishes.
        // The Operations must eventually call CompleteOperation, FailOperation, or EnterActive.
        protected abstract void Execute();

        public virtual bool TryCancel()
        {
            if (ending || !CanCancel()) { return false; }

            ending = true;
            OnCancelled();

            gate.EndOperation(this, SkipgateOperationEnd.Cancelled, heatGenerated: CancelHeat);

            return true;
        }

        protected virtual bool CanCancel()
        {
            return true;
        }

        protected void CompleteOperation(float heatGenerated)
        {
            if (ending)
            {
                return;
            }

            ending = true;
            OnCompleted();

            gate.EndOperation(this, SkipgateOperationEnd.Completed, heatGenerated);
        }

        protected void FailOperation(string reason)
        {
            if (ending)
            {
                return;
            }

            ending = true;
            OnFailed(reason);

            gate.EndOperation(this, SkipgateOperationEnd.Failed, heatGenerated: 0f);
        }

        protected virtual void OnCompleted() { }

        protected virtual void OnCancelled() { }

        protected virtual void OnFailed(string reason) { }

        public virtual void RestoreAfterLoad()
        {
            ApplyRuntimeState();
        }

        // Runs after the first tick (usually). Might get GameComponent to do it during FinalizeInit so it
        // doesn't constantly need checking in CompTick.
        public virtual void ResumeAfterLoad()
        {
        }

        protected virtual void ApplyRuntimeState()
        {
            if (UsesCapacitor
                && (phase == SkipgateOperationPhase.Preparing
                    || phase == SkipgateOperationPhase.Dialing))
            {
                gate.Capacitor.SetTarget(requiredCharge);
            }
        }

        protected bool TrySpendRequiredCharge()
        {
            if (!UsesCapacitor || gate.TrySpendCharge(requiredCharge)) { return true; }

            FailOperation("Insufficient charge at execution.");
            return false;
        }

        public virtual void ExposeData()
        {
            Scribe_Values.Look(ref phase, "phase", SkipgateOperationPhase.Preparing);
            Scribe_Values.Look(ref requiredCharge, "requiredCharge", 0f);
            Scribe_Values.Look(ref dialingTicksLeft, "dialingTicksLeft", 0);
        }
    }
}