using System;
using System.Collections.Generic;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// Pure split-screen decision logic. No UnityEngine types, so it can be
    /// unit-tested on any machine.
    ///
    /// The director answers three questions each frame:
    ///   1. should the view be merged, split in two, or split per player?
    ///   2. which players belong in which viewport?
    ///   3. what world centre and orthographic half-height does each viewport need?
    ///
    /// Hysteresis (merge &lt; split distance) stops the view flapping at the
    /// threshold; a dwell time stops Split2/Split4 oscillation.
    /// </summary>
    internal static class SplitScreenMath
    {
        internal struct Vec2
        {
            internal readonly float X;
            internal readonly float Y;

            internal Vec2(float x, float y)
            {
                X = x;
                Y = y;
            }
        }

        internal struct Viewport
        {
            internal readonly float X;
            internal readonly float Y;
            internal readonly float W;
            internal readonly float H;

            internal Viewport(float x, float y, float w, float h)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
            }
        }

        internal enum Mode
        {
            Merged,
            Split2,
            Split4
        }

        internal struct Config
        {
            internal float MergeDistance;
            internal float SplitDistance;
            internal float Padding;
            internal float MinSize;
            internal float MaxSize;
            internal float ScreenAspect;
            internal float DwellSeconds;
        }

        internal struct Plan
        {
            internal Mode Mode;
            internal Viewport[] Viewports;
            internal Vec2[] Centers;
            internal float[] Sizes;
            internal int[] PlayerViewport;
            internal int[] ClusterOfPlayer;
            internal float Spread;
        }

        private const float Epsilon = 0.0001f;

        internal static Plan Solve(Vec2[] players, Mode previous, float timeInMode, Config cfg)
        {
            int count = players == null ? 0 : players.Length;
            if (count <= 1)
                return MergedPlan(players, cfg);

            float spread = MaxPairwiseSpread(players);

            Mode candidate;
            if (previous == Mode.Merged)
            {
                candidate = spread >= cfg.SplitDistance
                    ? ChooseSplit(players, cfg)
                    : Mode.Merged;
            }
            else
            {
                candidate = spread <= cfg.MergeDistance
                    ? Mode.Merged
                    : ChooseSplit(players, cfg);
            }

            // Do not oscillate between the two split layouts immediately.
            if (candidate != Mode.Merged
                && previous != Mode.Merged
                && candidate != previous
                && timeInMode < cfg.DwellSeconds)
            {
                candidate = previous;
            }

            return BuildPlan(candidate, players, cfg, spread);
        }

        private static Mode ChooseSplit(Vec2[] players, Config cfg)
        {
            if (players.Length == 2)
                return Mode.Split2;

            int[] a;
            int[] b;
            Partition(players, out a, out b);
            if (a.Length == 0 || b.Length == 0)
                return Mode.Split4;

            bool vertical = Math.Abs(CentreX(players, a) - CentreX(players, b))
                            >= Math.Abs(CentreY(players, a) - CentreY(players, b));

            Viewport[] layout = Split2Layout(vertical);
            float sizeA = NeededSize(players, a, layout[0], cfg);
            float sizeB = NeededSize(players, b, layout[1], cfg);

            if (Math.Max(sizeA, sizeB) <= cfg.MaxSize + Epsilon)
                return Mode.Split2;

            // The dominant-axis halves were too narrow/short; try the other way.
            Viewport[] alternate = Split2Layout(!vertical);
            sizeA = NeededSize(players, a, alternate[0], cfg);
            sizeB = NeededSize(players, b, alternate[1], cfg);
            if (Math.Max(sizeA, sizeB) <= cfg.MaxSize + Epsilon)
                return Mode.Split2;

            return Mode.Split4;
        }

        private static Plan BuildPlan(Mode mode, Vec2[] players, Config cfg, float spread)
        {
            switch (mode)
            {
                case Mode.Split2:
                    return Split2Plan(players, cfg, spread);
                case Mode.Split4:
                    return Split4Plan(players, cfg, spread);
                default:
                    return MergedPlan(players, cfg);
            }
        }

        private static Plan MergedPlan(Vec2[] players, Config cfg)
        {
            if (players == null || players.Length == 0)
            {
                return new Plan
                {
                    Mode = Mode.Merged,
                    Viewports = new[] { FullViewport() },
                    Centers = new[] { new Vec2(0f, 0f) },
                    Sizes = new[] { cfg.MinSize },
                    PlayerViewport = new int[0],
                    ClusterOfPlayer = new int[0],
                    Spread = 0f
                };
            }

            var full = FullViewport();
            int[] all = AllIndices(players.Length);
            return new Plan
            {
                Mode = Mode.Merged,
                Viewports = new[] { full },
                Centers = new[] { BoundsCentre(players, all) },
                Sizes = new[] { NeededSize(players, all, full, cfg) },
                PlayerViewport = new int[players.Length],
                ClusterOfPlayer = new int[players.Length],
                Spread = MaxPairwiseSpread(players)
            };
        }

        private static Plan Split2Plan(Vec2[] players, Config cfg, float spread)
        {
            int[] a;
            int[] b;
            Partition(players, out a, out b);
            if (a.Length == 0 || b.Length == 0)
                return MergedPlan(players, cfg);

            bool vertical = Math.Abs(CentreX(players, a) - CentreX(players, b))
                            >= Math.Abs(CentreY(players, a) - CentreY(players, b));

            Viewport[] layout = Split2Layout(vertical);
            float sizeA = NeededSize(players, a, layout[0], cfg);
            float sizeB = NeededSize(players, b, layout[1], cfg);
            if (Math.Max(sizeA, sizeB) > cfg.MaxSize + Epsilon)
            {
                Viewport[] alternate = Split2Layout(!vertical);
                float altA = NeededSize(players, a, alternate[0], cfg);
                float altB = NeededSize(players, b, alternate[1], cfg);
                if (Math.Max(altA, altB) < Math.Max(sizeA, sizeB))
                {
                    vertical = !vertical;
                    layout = alternate;
                    sizeA = altA;
                    sizeB = altB;
                }
            }

            // Spatial order: left before right, top before bottom.
            bool swap = vertical
                ? CentreX(players, a) > CentreX(players, b)
                : CentreY(players, a) < CentreY(players, b);

            int[] first = swap ? b : a;
            int[] second = swap ? a : b;
            if (swap)
            {
                float tmp = sizeA; sizeA = sizeB; sizeB = tmp;
            }

            var playerViewport = new int[players.Length];
            var clusterOf = new int[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                playerViewport[i] = -1;
                clusterOf[i] = 0;
            }
            foreach (int index in first)
                playerViewport[index] = 0;
            foreach (int index in second)
            {
                playerViewport[index] = 1;
                clusterOf[index] = 1;
            }

            return new Plan
            {
                Mode = Mode.Split2,
                Viewports = layout,
                Centers = new[] { BoundsCentre(players, first), BoundsCentre(players, second) },
                Sizes = new[] { sizeA, sizeB },
                PlayerViewport = playerViewport,
                ClusterOfPlayer = clusterOf,
                Spread = spread
            };
        }

        private static Plan Split4Plan(Vec2[] players, Config cfg, float spread)
        {
            int count = Math.Min(players.Length, 4);
            int[] order;
            Viewport[] layout;

            if (count == 3)
            {
                // 1 + 2 layout: the leftmost player gets the left half, the
                // other two split the right half. Three thin panes read worse,
                // and leaving a quadrant empty wastes a quarter of the screen.
                int leftmost = 0;
                for (int i = 1; i < players.Length; i++)
                {
                    if (players[i].X < players[leftmost].X
                        || (Math.Abs(players[i].X - players[leftmost].X) < Epsilon
                            && players[i].Y > players[leftmost].Y))
                    {
                        leftmost = i;
                    }
                }

                var rest = new List<int>(2);
                for (int i = 0; i < players.Length; i++)
                {
                    if (i != leftmost)
                        rest.Add(i);
                }
                rest.Sort((x, y) => players[y].Y.CompareTo(players[x].Y));

                order = new[] { leftmost, rest[0], rest[1] };
                layout = new[]
                {
                    new Viewport(0f, 0f, 0.5f, 1f),       // left half
                    new Viewport(0.5f, 0.5f, 0.5f, 0.5f), // right-top
                    new Viewport(0.5f, 0f, 0.5f, 0.5f)    // right-bottom
                };
            }
            else
            {
                Viewport[] quadrants =
                {
                    new Viewport(0f, 0.5f, 0.5f, 0.5f),   // top-left
                    new Viewport(0.5f, 0.5f, 0.5f, 0.5f), // top-right
                    new Viewport(0f, 0f, 0.5f, 0.5f),      // bottom-left
                    new Viewport(0.5f, 0f, 0.5f, 0.5f)     // bottom-right
                };

                order = new int[count];
                for (int i = 0; i < count; i++)
                    order[i] = i;

                // Reading order: higher on screen first, then left to right.
                Array.Sort(order, (x, y) =>
                {
                    int byY = players[y].Y.CompareTo(players[x].Y);
                    if (byY != 0) return byY;
                    int byX = players[x].X.CompareTo(players[y].X);
                    return byX != 0 ? byX : x.CompareTo(y);
                });

                layout = quadrants;
            }

            var viewports = new Viewport[count];
            var centers = new Vec2[count];
            var sizes = new float[count];
            var playerViewport = new int[players.Length];

            for (int v = 0; v < count; v++)
            {
                int playerIndex = order[v];
                viewports[v] = layout[v];
                centers[v] = players[playerIndex];
                sizes[v] = cfg.MinSize;
                playerViewport[playerIndex] = v;
            }

            // Players beyond four (should not happen) stay on the last viewport.
            for (int i = 0; i < players.Length; i++)
            {
                if (playerViewport[i] < 0)
                    playerViewport[i] = count - 1;
            }

            return new Plan
            {
                Mode = Mode.Split4,
                Viewports = viewports,
                Centers = centers,
                Sizes = sizes,
                PlayerViewport = playerViewport,
                ClusterOfPlayer = playerViewport,
                Spread = spread
            };
        }

        private static Viewport FullViewport()
        {
            return new Viewport(0f, 0f, 1f, 1f);
        }

        private static Viewport[] Split2Layout(bool vertical)
        {
            return vertical
                ? new[] { new Viewport(0f, 0f, 0.5f, 1f), new Viewport(0.5f, 0f, 0.5f, 1f) }
                : new[] { new Viewport(0f, 0.5f, 1f, 0.5f), new Viewport(0f, 0f, 1f, 0.5f) };
        }

        /// <summary>
        /// PCA partition: project onto the principal axis, sort, split at the
        /// largest gap. With two players the partition is simply one each.
        /// </summary>
        private static void Partition(Vec2[] players, out int[] a, out int[] b)
        {
            int n = players.Length;
            if (n <= 1)
            {
                a = AllIndices(n);
                b = new int[0];
                return;
            }
            if (n == 2)
            {
                a = new[] { 0 };
                b = new[] { 1 };
                return;
            }

            float cx = 0f, cy = 0f;
            for (int i = 0; i < n; i++)
            {
                cx += players[i].X;
                cy += players[i].Y;
            }
            cx /= n;
            cy /= n;

            float cxx = 0f, cyy = 0f, cxy = 0f;
            for (int i = 0; i < n; i++)
            {
                float dx = players[i].X - cx;
                float dy = players[i].Y - cy;
                cxx += dx * dx;
                cyy += dy * dy;
                cxy += dx * dy;
            }

            float angle = 0.5f * (float)Math.Atan2(2f * cxy, cxx - cyy);
            float ax = (float)Math.Cos(angle);
            float ay = (float)Math.Sin(angle);

            var order = new int[n];
            var projection = new float[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                projection[i] = (players[i].X - cx) * ax + (players[i].Y - cy) * ay;
            }
            Array.Sort(order, (x, y) => projection[x].CompareTo(projection[y]));

            int split = n / 2;
            float largestGap = -1f;
            for (int i = 1; i < n; i++)
            {
                float gap = projection[order[i]] - projection[order[i - 1]];
                if (gap > largestGap)
                {
                    largestGap = gap;
                    split = i;
                }
            }
            if (largestGap < Epsilon)
                split = n / 2;

            split = Math.Max(1, Math.Min(n - 1, split));
            a = new int[split];
            b = new int[n - split];
            Array.Copy(order, 0, a, 0, split);
            Array.Copy(order, split, b, 0, n - split);
        }

        private static float NeededSize(Vec2[] players, int[] indices, Viewport viewport, Config cfg)
        {
            float minX, maxX, minY, maxY;
            Bounds(players, indices, out minX, out maxX, out minY, out maxY);

            float aspect = cfg.ScreenAspect * (viewport.W / Math.Max(Epsilon, viewport.H));
            float halfHeight = (maxY - minY + 2f * cfg.Padding) * 0.5f;
            float halfWidth = (maxX - minX + 2f * cfg.Padding) / (2f * Math.Max(Epsilon, aspect));

            return Math.Max(cfg.MinSize, Math.Max(halfHeight, halfWidth));
        }

        private static void Bounds(Vec2[] players, int[] indices, out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = float.MaxValue;
            maxX = float.MinValue;
            minY = float.MaxValue;
            maxY = float.MinValue;

            foreach (int i in indices)
            {
                Vec2 p = players[i];
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            if (minX > maxX)
            {
                minX = maxX = 0f;
                minY = maxY = 0f;
            }
        }

        private static Vec2 BoundsCentre(Vec2[] players, int[] indices)
        {
            float minX, maxX, minY, maxY;
            Bounds(players, indices, out minX, out maxX, out minY, out maxY);
            return new Vec2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        }

        private static float CentreX(Vec2[] players, int[] indices)
        {
            float sum = 0f;
            foreach (int i in indices)
                sum += players[i].X;
            return sum / Math.Max(1, indices.Length);
        }

        private static float CentreY(Vec2[] players, int[] indices)
        {
            float sum = 0f;
            foreach (int i in indices)
                sum += players[i].Y;
            return sum / Math.Max(1, indices.Length);
        }

        /// <summary>
        /// Largest pairwise distance between players. Exposed so the Unity
        /// glue can skip Solve entirely while everyone is comfortably merged.
        /// </summary>
        internal static float MaxSpread(Vec2[] players)
        {
            if (players == null || players.Length < 2)
                return 0f;
            return MaxPairwiseSpread(players);
        }

        private static float MaxPairwiseSpread(Vec2[] players)
        {
            float best = 0f;
            for (int i = 0; i < players.Length; i++)
            {
                for (int j = i + 1; j < players.Length; j++)
                {
                    float dx = players[i].X - players[j].X;
                    float dy = players[i].Y - players[j].Y;
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (distance > best)
                        best = distance;
                }
            }
            return best;
        }

        private static int[] AllIndices(int n)
        {
            var result = new int[n];
            for (int i = 0; i < n; i++)
                result[i] = i;
            return result;
        }
    }
}
