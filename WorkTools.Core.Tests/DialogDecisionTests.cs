using WorkTools.Desktop;

namespace WorkTools.Core.Tests;

[TestClass]
public class DialogDecisionTests
{
    [DataTestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(null, false)]
    public void IsConfirmed_ReturnsExpected(bool? decision, bool expected)
    {
        bool actual = DialogDecision.IsConfirmed(decision);

        Assert.AreEqual(expected, actual);
    }
}
