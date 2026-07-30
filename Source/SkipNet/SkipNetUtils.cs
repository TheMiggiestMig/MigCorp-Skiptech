using HarmonyLib;
using MigCorp.Skiptech.Utils;
using System;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MigCorp.Skiptech.SkipNet
{
    public static class SkipNetUtils
    {
        // Special accessors to dig into a given Pawn_PathFollower's private fields.
        // Moved to SkipNetUtils since I'm now using it for more than just the patches.
        public static readonly AccessTools.FieldRef<Pawn_PathFollower, LocalTargetInfo>
        _patherDestRef = AccessTools.FieldRefAccess<Pawn_PathFollower, LocalTargetInfo>("destination");
        public static readonly AccessTools.FieldRef<Pawn_PathFollower, PathEndMode>
        _patherPeModeRef = AccessTools.FieldRefAccess<Pawn_PathFollower, PathEndMode>("peMode");

        // Read-only views.
        public static LocalTargetInfo PatherDest(Pawn_PathFollower pather) => _patherDestRef(pather);
        public static PathEndMode PatherPeMode(Pawn_PathFollower pather) => _patherPeModeRef(pather);

        public static int OctileDistance(IntVec3 start, IntVec3 end)
        {
            IntVec3 d = start - end;
            int dx = Math.Abs(d.x);
            int dz = Math.Abs(d.z);
            return Math.Max(dx, dz) * 10 + Math.Min(dx, dz) * 4;
        }

        internal static void TeleportPawn(Pawn pawn, IntVec3 position)
        {
            FxUtil.PlaySkip(pawn.Position, pawn.Map, true);
            pawn.Position = position;
            pawn.Drawer.tweener.Notify_Teleported();
            FxUtil.PlaySkip(pawn.Position, pawn.Map, true);
        }
    }

    public sealed class Deque<T>
    {
        private T[] buffer;
        private int head;
        private int count;

        public Deque(int capacity = 4)
        {
            buffer = new T[Mathf.Max(4, capacity)];
        }

        public int Count => count;

        public void Clear()
        {
            head = 0;
            count = 0;
        }

        public void AddFirst(T item)
        {
            Ensure(count + 1);
            // move head back by ONE, wrap safely
            head = (head - 1 + buffer.Length) % buffer.Length;
            buffer[head] = item;
            count++;
        }

        public void AddLast(T item)
        {
            Ensure(count + 1);
            int tail = (head + count) % buffer.Length;
            buffer[tail] = item;
            count++;
        }

        public T PopFirst()
        {
            if (count == 0) throw new Exception("Deque empty");
            T item = buffer[head];
            head = (head + 1) % buffer.Length;
            count--;
            return item;
        }

        public T PeekFirst()
        {
            if (count == 0) throw new Exception("Deque empty");
            return buffer[head];
        }

        private void Ensure(int want)
        {
            if (want <= buffer.Length) return;
            int newCap = buffer.Length;
            while (newCap < want) newCap *= 2;

            T[] newBuffer = new T[newCap];
            // copy from old buffer BEFORE swapping
            for (int i = 0; i < count; i++)
                newBuffer[i] = buffer[(head + i) % buffer.Length];

            buffer = newBuffer;
            head = 0;
        }
    }
}
