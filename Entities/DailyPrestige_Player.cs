using SQLite;
using System.Collections.Generic;

namespace DailyPrestige.Entities
{
    public class DailyPrestige_Player : ModKit.ORM.ModEntity<DailyPrestige_Player>
    {
        [AutoIncrement][PrimaryKey] public int Id { get; set; }
        public string SteamId { get; set; }
        public string CharacterFullName { get; set; }
        public int Prestige { get; set; }
        public int LastDateTaskCompleted {  get; set; }

        public string RewardRecovered { get; set; }
        [Ignore]
        public List<int> LRewardRecovered { get; set; } = new List<int>();
        public DailyPrestige_Player() { }
    }
}
