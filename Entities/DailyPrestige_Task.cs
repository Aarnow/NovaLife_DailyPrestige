using ModKit.Utils;
using SQLite;
namespace DailyPrestige.Entities
{
    public class DailyPrestige_Task : ModKit.ORM.ModEntity<DailyPrestige_Task>
    {
        [AutoIncrement][PrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
        public int ItemId { get; set; }
        public int Quantity { get; set; }
        public int Date { get; set; }
        public int ResolvedCounter { get; set; }
        public int ObjectiveCounter { get; set; }

        public DailyPrestige_Task() { }
    }
}
