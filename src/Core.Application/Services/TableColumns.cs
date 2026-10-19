using Core.Domain.Models;
namespace Core.Application.Services;

public static class TableColumns
{
    public static ColumnLayout[] Display(IReadOnlyList<ColumnLayout> layout, double viewportWidth, bool explicitWidths)
    {
        var visible = layout.Where(c => c.Visible).OrderBy(c => c.Order).ToArray();
        var flexible = visible.FirstOrDefault(c => c.Name == "Source") ?? visible.FirstOrDefault(c => c.Name == "Unit / meaning")
            ?? visible.FirstOrDefault(c => c.Name == "Value") ?? visible.FirstOrDefault();
        var spare = explicitWidths ? 0 : Math.Max(0, viewportWidth - 16 - visible.Sum(c => c.Width));
        return layout.Select(c => c == flexible ? c with { Width = c.Width + spare } : c).ToArray();
    }
}
