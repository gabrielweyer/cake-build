using Contoso.Hello.SuperLogic;
using Xunit;

namespace Contoso.Hello.HelloTests
{
    public class EvenMoreUselessTests
    {
        [Fact]
        public void WhenDoWork_ThenSomeSweetJson()
        {
            // Act

            var actual = EvenMoreUseless.DoWork();
            
            // Assert
            
            Assert.Equal("Some JSON (maybe): \"Hello\"", actual);
        }
    }
}