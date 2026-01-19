using UnityEngine;
using Verse;
using System;
using MigCorp.Skiptech.Utils;

namespace MigCorp.Skiptech.SkipNet
{
    public static class SkipNetUtils
    {
        public static int OctileDistance(IntVec3 start, IntVec3 end)
        {
            IntVec3 d = start - end;
            int dx = Mathf.Abs(d.x);
            int dz = Mathf.Abs(d.z);
            return Mathf.Min(dx, dz) * 10 + Mathf.Abs(dx - dz) * 4;
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
