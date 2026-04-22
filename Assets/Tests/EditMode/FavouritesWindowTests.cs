using System.Reflection;
using NUnit.Framework;
using UnityCodeMcpServer.Editor.EditorTools;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class FavouritesWindowTests
    {
        [Test]
        public void CalculateScriptContentHeight_ReturnsMinimumHeight_ForShortViewport()
        {
            var method = typeof(FavouritesWindow).GetMethod(
                "CalculateScriptContentHeight",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, "Expected script content height to be calculated separately from the scroll view layout.");

            var result = (float)method.Invoke(null, new object[] { 120f });

            Assert.That(result, Is.EqualTo(120f).Within(0.01f));
        }

        [Test]
        public void CalculateScriptContentHeight_ReturnsViewportHeight_ForTallViewport()
        {
            var method = typeof(FavouritesWindow).GetMethod(
                "CalculateScriptContentHeight",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, "Expected script content height to be calculated separately from the scroll view layout.");

            var result = (float)method.Invoke(null, new object[] { 540f });

            Assert.That(result, Is.EqualTo(540f).Within(0.01f));
        }
    }
}
