using SQLite;

namespace DailyPrestige.Entities
{
    public class DailyPrestige_Reward : ModKit.ORM.ModEntity<DailyPrestige_Reward>
    {
        [AutoIncrement][PrimaryKey] public int Id { get; set; }
        public int ItemId { get; set; }
        public int ItemQuantity { get; set; }
        public int PrestigeRequired { get; set; }
        public DailyPrestige_Reward() { }
    }
}
