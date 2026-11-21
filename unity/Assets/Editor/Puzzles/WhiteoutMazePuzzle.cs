// Whiteout Maze (puzzle 18, blind mask): a maze of snow walls, shoulder high, seven cells by seven. The walkers put
// on the blindfold and go in through the blind turnstile; a lookout climbs the platform beside it, sees the whole
// maze from above and calls the turns. Reaching the far side's exit wins it. The maze is the same every time (its
// walls are generated from a fixed seed); a walker who gives up takes the blindfold off and walks out.
// No custom code.
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class WhiteoutMazePuzzle : PuzzleDef
{
    public override string Name => "WhiteoutMaze";
    public override Vector2 Footprint => new Vector2(30f, 32f);
    public override Vector2? FixedSite => new Vector2(-710f, -1250f);      // chosen on the island's lowland; its build levels and clears it

    const int N = 7;
    const float CellSize = 2.6f, WallH = 1.75f, WallT = 0.45f;

    public override void Build(PuzzleBuilder b)
    {
        b.DebugTeleport(0f, -15f);
        b.ClearGround(30f, 32f);
        Material snowy = IsleKit.Snowy(b);

        // ---- the maze: a depth-first carve from the entrance cell, from a fixed seed ----
        var random = new System.Random(1812);
        bool[,] east = new bool[N, N], north = new bool[N, N];      // true where the wall to the east / north of a cell is open
        var seen = new bool[N, N];
        var stack = new Stack<(int x, int z)>();
        stack.Push((N / 2, 0)); seen[N / 2, 0] = true;
        while (stack.Count > 0)
        {
            var (cx, cz) = stack.Peek();
            var options = new List<(int x, int z)>();
            if (cx > 0 && !seen[cx - 1, cz]) options.Add((cx - 1, cz));
            if (cx < N - 1 && !seen[cx + 1, cz]) options.Add((cx + 1, cz));
            if (cz > 0 && !seen[cx, cz - 1]) options.Add((cx, cz - 1));
            if (cz < N - 1 && !seen[cx, cz + 1]) options.Add((cx, cz + 1));
            if (options.Count == 0) { stack.Pop(); continue; }
            var (nx, nz) = options[random.Next(options.Count)];
            if (nx != cx) east[Mathf.Min(cx, nx), cz] = true; else north[cx, Mathf.Min(cz, nz)] = true;
            seen[nx, nz] = true;
            stack.Push((nx, nz));
        }
        float x0 = -N * CellSize / 2f, z0 = -N * CellSize / 2f;
        var walls = new MeshBuilder();
        var snowBlock = new Color(0.92f, 0.94f, 0.98f);
        void Wall(float ax, float az, float bx, float bz)
        {
            float len = Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(bz - az)) + WallT;
            bool alongX = Mathf.Abs(bx - ax) > Mathf.Abs(bz - az);
            Kit.AddBox(walls, new Vector3((ax + bx) / 2f, 0f, (az + bz) / 2f), alongX ? new Vector3(len, WallH, WallT) : new Vector3(WallT, WallH, len), snowBlock);
        }
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                float cx0 = x0 + i * CellSize, cz0 = z0 + j * CellSize;
                if (i < N - 1 && !east[i, j]) Wall(cx0 + CellSize, cz0, cx0 + CellSize, cz0 + CellSize);
                if (j < N - 1 && !north[i, j]) Wall(cx0, cz0 + CellSize, cx0 + CellSize, cz0 + CellSize);
            }
        // the outer wall, with the entrance in the middle of the south side and the way out in the middle of the north
        // (each a turnstile, with short wings)
        float xm = x0 + (N / 2) * CellSize;
        const float Gap = 1.07f;      // half the turnstile's width
        Wall(x0, z0, xm + CellSize / 2f - Gap - WallT / 2f, z0); Wall(xm + CellSize / 2f + Gap + WallT / 2f, z0, -x0, z0);
        Wall(x0, -z0, xm + CellSize / 2f - Gap - WallT / 2f, -z0); Wall(xm + CellSize / 2f + Gap + WallT / 2f, -z0, -x0, -z0);
        Wall(x0, z0, x0, -z0); Wall(-x0, z0, -x0, -z0);
        b.MeshPiece("Maze", walls.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        // ---- in: the blindfold and the blind turnstile; out: the exit's pad, which wins it ----
        IsleKit.BlindfoldHook(b, -3f, z0 - 3.2f, 0f);
        IsleKit.BlindGate(b, xm + CellSize / 2f, z0, 0f);
        // the goal is the last cell, inside the maze; out of it a plain one-way turnstile lets walkers leave, and
        // nobody comes in that way
        Piece goal = IsleKit.Sensor(b, "Goal", xm + CellSize / 2f, -z0 - CellSize / 2f, new Vector3(CellSize - 0.6f, 2.6f, CellSize - 0.6f));
        goal.On("PuzzlePresenceSensor", "onPressed", b.Puzzle, "Win");
        b.Part("Gate No items", xm + CellSize / 2f, -z0, 0f, 0f).Named("Way Out").Set("PuzzleNoItemsGate", "oneWay", true);
        var arch = new MeshBuilder();
        Kit.AddBox(arch, new Vector3(-1.3f, 0f, 0f), new Vector3(0.22f, 2.8f, 0.22f), IsleKit.Red);
        Kit.AddBox(arch, new Vector3(1.3f, 0f, 0f), new Vector3(0.22f, 2.8f, 0.22f), IsleKit.Red);
        Kit.AddBox(arch, new Vector3(0f, 2.8f, 0f), new Vector3(2.9f, 0.24f, 0.3f), IsleKit.Red);
        b.MeshPiece("Exit Arch", arch.ToMesh(), snowy).Transform.position = b.WorldPoint(xm + CellSize / 2f, -z0 + 0.4f);

        // ---- the lookout: a platform three and a half metres up beside the maze, with stairs ----
        const float Up = 3.6f;
        float lx = -x0 + 3.2f;
        var lookout = new MeshBuilder();
        Kit.AddBox(lookout, new Vector3(lx, 0f, 0f), new Vector3(3.2f, Up, 3.2f), IsleKit.Stone);
        for (int s = 1; s <= 9; s++)
            Kit.AddBox(lookout, new Vector3(lx, 0f, -1.6f - s * 0.45f), new Vector3(1.4f, Up * (1f - s / 10f), 0.45f), IsleKit.Stone);
        Kit.AddBox(lookout, new Vector3(lx + 1.55f, Up, 0f), new Vector3(0.1f, 1f, 3.2f), IsleKit.Wood);      // rails
        Kit.AddBox(lookout, new Vector3(lx, Up, 1.55f), new Vector3(3.2f, 1f, 0.1f), IsleKit.Wood);
        b.MeshPiece("Lookout", lookout.ToMesh(), snowy).Transform.position = b.WorldPoint(0f, 0f);

        IsleKit.StartCorner(b, -6f, z0 - 5f, 0f);
        Kit.LightRing(b, "Site Lights", 2f, 0f, 28f, 30f, 2.6f, true, 181);
    }
}
