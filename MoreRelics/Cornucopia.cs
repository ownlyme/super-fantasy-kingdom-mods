using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;
using SuperFantasyKingdom.Spawner;

namespace MoreRelics
{
	// ------------------------------ cornucopia ------------------------------
	internal sealed class Cornucopia : RelicEntry
	{
		private const int MinPerDay = 1;
		private const int MaxPerDay = 2;
		// keeps our stream clear of vanilla's, which salt the same seed with 1337, 334, 43215
		private const int SeedSalt = 6317;

		// gathered goods only
		private static readonly ResourceType[] Gathered =
		{
			ResourceType.Wood,
			ResourceType.Stone,
			ResourceType.Wheat,
			ResourceType.Berry,
			ResourceType.Fish,
			ResourceType.Gold,
			ResourceType.Raw
		};

		private static readonly RelicDef Definition = new RelicDef
		{
			id = "ownly_Cornucopia",
			cloneFrom = new[] { "GreenHouse", "BetterBerries", "Fruitarian", "SpawnTrees" },
			rarity = Rarity.Rare,
			cost = 6,
			title = "Cornucopia",
			description = "Every morning, 1-2 random resources appear."
		};

		public override RelicDef Def
		{
			get { return Definition; }
		}

		public override void OnMorning(int day)
		{
			// restarting the day replays the morning, so the roll hangs off the run seed and the day
			int seed = ((GameManager.Instance != null) ? GameManager.Instance.GetSeed() : 0);
			System.Random roll = new System.Random(seed * 7 + SeedSalt + day);
			BuildingCastle castle = ((CityManager.Instance != null) ? CityManager.Instance.GetCastle() : null);
			int count = roll.Next(MinPerDay, MaxPerDay + 1);
			for (int i = 0; i < count; i++)
			{
				ResourceType type = Gathered[roll.Next(Gathered.Length)];
				if (castle == null || DroppedShardSpawner.Instance == null)
				{
					ResourceManager.Instance.AddResource(type, 1);
					continue;
				}
				DroppedShardSpawner.Instance.Spawn(castle.GetPosition(), 1, type);
			}
		}
	}
}
