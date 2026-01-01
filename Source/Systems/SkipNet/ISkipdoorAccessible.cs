using Verse;

namespace MigCorp.Skiptech.Systems.SkipNet
{
    public interface ISkipdoorAccessible
    {
        /// <summary>
        /// Checks if the pawn is allowed to enter the skipdoor
        /// </summary>
        /// <param name="pawn"></param>
        bool CanEnter(Pawn pawn);

        /// <summary>
        /// Checks if the pawn is allowed to exit the skipdoor
        /// </summary>
        /// <param name="pawn"></param>
        bool CanExit(Pawn pawn);

        /// <summary>
        /// Checks if the pawn is able to enter the skipdoor right at this moment.
        /// </summary>
        /// <remarks>
        /// This is useful for things like adding a "charge-up" delay, or other soft restrictions.
        /// </remarks>
        /// <param name="pawn"></param>
        int TicksUntilEnterable(Pawn pawn);

        /// <summary>
        /// Checks if the pawn is able to exit the skipdoor right at this moment.
        /// </summary>
        /// <remarks>
        /// This is useful for things like adding a "charge-up" delay, or other soft restrictions.
        /// </remarks>
        /// <param name="pawn"></param>
        int TicksUntilExitable(Pawn pawn);

        /// <summary>
        /// Notifies the skipdoor that a pawn has arrived.
        /// </summary>
        /// <param name="pawn"></param>
        /// <param name="skipNetPlan"></param>
        void Notify_PawnArrived(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type = SkipdoorType.Entry);

        /// <summary>
        /// Notifies the skipdoor that a pawn has teleported.
        /// </summary>
        /// <param name="pawn"></param>
        /// <param name="skipNetPlan"></param>
        void Notify_PawnTeleported(Pawn pawn, SkipNetPlan skipNetPlan, SkipdoorType type = SkipdoorType.Entry);
    }
}