namespace BenMcLean.Wolf3D.VoxelBaker;

public static class VoxelAtlasPacker<T> where T : notnull
{
	public const byte Brick = 2, // Alignment requirement
		Gap = 2; // Minimum spacing requirement
	private class Node(int x, int y, int z, int w, int d, int h)
	{
		public int X = x, Y = y, Z = z, W = w, D = d, H = h;
		public bool Used;
		public Node? Child1, Child2;
	}
	public record Cuboid(T Id, int X, int Y, int Z);
	public record PackResult(int Width, int Depth, int Height, List<Cuboid> Placements);
	public static PackResult Pack(IEnumerable<Cuboid> cuboids)
	{
		// 1. Transform dimensions into "Brick Space"
		// We add the gap first, then round up to the nearest brick size.
		List<Cuboid> items = [.. cuboids
			.Select(c => new Cuboid(
				Id: c.Id,
				X: Align(c.X + Gap, Brick) / Brick,
				Y: Align(c.Y + Gap, Brick) / Brick,
				Z: Align(c.Z + Gap, Brick) / Brick))
			.OrderByDescending(c => c.X * c.Y * c.Z)];
		if (items.Count == 0)
			throw new ArgumentException("Nothing to pack!", nameof(cuboids));
		// 2. Initial Setup
		int maxDim = items.Max(i => Math.Max(i.X, Math.Max(i.Y, i.Z))),
			rootSize = Math.Max(maxDim, EstimateInitialSize(items));
		Node root = new(0, 0, 0, rootSize, rootSize, rootSize);
		List<Cuboid> placements = [];
		// 3. Packing Loop
		foreach (Cuboid cuboid in items)
		{
			Node? node = FindNode(root, cuboid.X, cuboid.Y, cuboid.Z);
			while (node is null)
			{
				Grow(ref root);
				node = FindNode(root, cuboid.X, cuboid.Y, cuboid.Z);
			}
			Node placed = SplitNode(node, cuboid.X, cuboid.Y, cuboid.Z);
			// Transform back from Brick Space to Voxel Space
			placements.Add(new Cuboid(
				Id: cuboid.Id,
				X: placed.X * Brick,
				Y: placed.Y * Brick,
				Z: placed.Z * Brick));
		}
		return new(
			Width: root.W * Brick - Gap,
			Depth: root.D * Brick - Gap,
			Height: root.H * Brick - Gap,
			Placements: placements);
	}
	private static Node? FindNode(Node node, int w, int d, int h) =>
		// If node has children, it's not a leaf; recurse down.
		node.Child1 is not null && node.Child2 is not null
			? FindNode(node.Child1, w, d, h)
				?? FindNode(node.Child2, w, d, h)
			// If it's already used or physically too small, it's a no-go.
			: node.Used || w > node.W || d > node.D || h > node.H
				? null
				: node;
	private static Node SplitNode(Node node, int w, int d, int h)
	{
		// If the node is an exact fit, claim it.
		if (node.W == w && node.D == d && node.H == h)
		{
			node.Used = true;
			return node;
		}
		// Otherwise, split the node into two based on the most beneficial axis.
		int dw = node.W - w,
			dd = node.D - d,
			dh = node.H - h;
		if (dw >= dd && dw >= dh)
		{// Split along Width
			node.Child1 = new Node(node.X, node.Y, node.Z, w, node.D, node.H);
			node.Child2 = new Node(node.X + w, node.Y, node.Z, dw, node.D, node.H);
		}
		else if (dd >= dh)
		{// Split along Depth
			node.Child1 = new Node(node.X, node.Y, node.Z, node.W, d, node.H);
			node.Child2 = new Node(node.X, node.Y + d, node.Z, node.W, dd, node.H);
		}
		else
		{// Split along Height
			node.Child1 = new Node(node.X, node.Y, node.Z, node.W, node.D, h);
			node.Child2 = new Node(node.X, node.Y, node.Z + h, node.W, node.D, dh);
		}
		// Recursively refine the first child until it's the exact size needed.
		return SplitNode(node.Child1, w, d, h);
	}
	private static void Grow(ref Node root) =>
		root = root.W <= root.D && root.W <= root.H
			? new(0, 0, 0, root.W + 1, root.D, root.H)
			{
				Child1 = root,
				Child2 = new(root.W, 0, 0, 1, root.D, root.H)
			}
			: root.D <= root.H
			? new(0, 0, 0, root.W, root.D + 1, root.H)
			{
				Child1 = root,
				Child2 = new(0, root.D, 0, root.W, 1, root.H)
			}
			: new(0, 0, 0, root.W, root.D, root.H + 1)
			{
				Child1 = root,
				Child2 = new(0, 0, root.H, root.W, root.D, 1)
			};
	private static int Align(int value, int alignment) =>
		(value + alignment - 1) / alignment * alignment;
	private static int EstimateInitialSize(IEnumerable<Cuboid> cuboids) =>
		(int)Math.Max(8, Math.Cbrt(cuboids.Sum(i => (double)i.X * i.Y * i.Z)));
}
