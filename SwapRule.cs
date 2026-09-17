using System.Windows.Input;

namespace Click2Key.Models;

public sealed class SwapRule
{
    public Key First { get; set; } = Key.Back;
    public Key Second { get; set; } = Key.A;
    public bool Enabled { get; set; } = true;
}
