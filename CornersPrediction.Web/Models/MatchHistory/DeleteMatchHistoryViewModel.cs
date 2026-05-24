using System.ComponentModel.DataAnnotations;

namespace CornersPrediction.Web.Models.MatchHistory;

public sealed class DeleteMatchHistoryViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }
}
