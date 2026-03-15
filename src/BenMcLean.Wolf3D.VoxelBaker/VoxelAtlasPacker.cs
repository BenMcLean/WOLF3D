namespace BenMcLean.Wolf3D.VoxelBaker;

public static class VoxelAtlasPacker<T>
{
	public const int Brick = 2, // Alignment requirement
		Gap = 2; // Minimum spacing requirement
	private class Node(int x, int y, int z, int w, int h, int d)
	{
		public int X = x, Y = y, Z = z, W = w, H = h, D = d;
		public bool Used;
		public Node? Child1, Child2;
	}
	public record Cuboid(T Id, int Width, int Height, int Depth);
	public record Placement(T Id, int X, int Y, int Z);
	public record PackResult(int Width, int Height, int Depth, List<Placement> Placements);
	public static PackResult Pack(IEnumerable<Cuboid> cuboids)
	{
		// 1. Transform dimensions into "Brick Space"
		// We add the gap first, then round up to the nearest brick size.
		List<Cuboid> items = [.. cuboids
			.Select(c => new Cuboid(
				Id: c.Id,
				Width: Align(c.Width + Gap, Brick) / Brick,
				Height: Align(c.Height + Gap, Brick) / Brick,
				Depth: Align(c.Depth + Gap, Brick) / Brick))
			.OrderByDescending(c => c.Width * c.Height * c.Depth)];
		if (items.Count == 0)
			throw new ArgumentException("Nothing to pack!", nameof(cuboids));
		// 2. Initial Setup
		int maxDim = items.Max(i => Math.Max(i.Width, Math.Max(i.Height, i.Depth))),
			rootSize = Math.Max(maxDim, EstimateInitialSize(items));
		Node root = new(0, 0, 0, rootSize, rootSize, rootSize);
		List<Placement> placements = [];
		// 3. Packing Loop
		foreach (Cuboid cuboid in items)
		{
			Node? node = FindNode(root, cuboid.Width, cuboid.Height, cuboid.Depth);
			while (node is null)
			{
				Grow(ref root);
				node = FindNode(root, cuboid.Width, cuboid.Height, cuboid.Depth);
			}
			Node placed = SplitNode(node, cuboid.Width, cuboid.Height, cuboid.Depth);
			// Transform back from Brick Space to Voxel Space
			placements.Add(new Placement(
				Id: cuboid.Id,
				X: placed.X * Brick,
				Y: placed.Y * Brick,
				Z: placed.Z * Brick));
		}
		return new(
			Width: root.W * Brick - Gap,
			Height: root.H * Brick - Gap,
			Depth: root.D * Brick - Gap,
			Placements: placements);
	}
	private static Node? FindNode(Node node, int w, int h, int d) =>
		// If node has children, it's not a leaf; recurse down.
		node.Child1 is not null && node.Child2 is not null
			? FindNode(node.Child1, w, h, d)
				?? FindNode(node.Child2, w, h, d)
			// If it's already used or physically too small, it's a no-go.
			: node.Used || w > node.W || h > node.H || d > node.D
				? null
				: node;
	private static Node SplitNode(Node node, int w, int h, int d)
	{
		// If the node is an exact fit, claim it.
		if (node.W == w && node.H == h && node.D == d)
		{
			node.Used = true;
			return node;
		}
		// Otherwise, split the node into two based on the most beneficial axis.
		int dw = node.W - w,
			dh = node.H - h,
			dd = node.D - d;
		if (dw >= dh && dw >= dd)
		{// Split along Width
			node.Child1 = new Node(node.X, node.Y, node.Z, w, node.H, node.D);
			node.Child2 = new Node(node.X + w, node.Y, node.Z, dw, node.H, node.D);
		}
		else if (dh >= dd)
		{// Split along Height
			node.Child1 = new Node(node.X, node.Y, node.Z, node.W, h, node.D);
			node.Child2 = new Node(node.X, node.Y + h, node.Z, node.W, dh, node.D);
		}
		else
		{// Split along Depth
			node.Child1 = new Node(node.X, node.Y, node.Z, node.W, node.H, d);
			node.Child2 = new Node(node.X, node.Y, node.Z + d, node.W, node.H, dd);
		}
		// Recursively refine the first child until it's the exact size needed.
		return SplitNode(node.Child1, w, h, d);
	}
	private static void Grow(ref Node root) =>
		root = root.W <= root.H && root.W <= root.D
			? new(0, 0, 0, root.W + 1, root.H, root.D)
			{
				Child1 = root,
				Child2 = new(root.W, 0, 0, 1, root.H, root.D)
			}
			: root.H <= root.D
			? new(0, 0, 0, root.W, root.H + 1, root.D)
			{
				Child1 = root,
				Child2 = new(0, root.H, 0, root.W, 1, root.D)
			}
			: new(0, 0, 0, root.W, root.H, root.D + 1)
			{
				Child1 = root,
				Child2 = new(0, 0, root.D, root.W, root.H, 1)
			};
	private static int Align(int value, int alignment) =>
		(value + alignment - 1) / alignment * alignment;
	private static int EstimateInitialSize(IEnumerable<Cuboid> cuboids) =>
		(int)Math.Max(8, Math.Cbrt(cuboids.Sum(i => (double)i.Width * i.Height * i.Depth)));
}
