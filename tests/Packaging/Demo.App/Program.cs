using Demo.Services;
using InterfaceGenerator;
using Microsoft.Extensions.DependencyInjection;

// Generated here, from an attribute that arrived transitively through Demo.Services.
[GenerateInterface]
public partial class Greeter
{
    public string Hello() => "hello";
}

public static class Program
{
    public static void Main()
    {
        ServiceProvider provider = new ServiceCollection()
            // Two generated methods, one per assembly: they do not collide because each is named
            // after the assembly it was generated in.
            .AddDemoAppServices()
            .AddDemoServicesServices()
            .BuildServiceProvider();

        using (provider)
        {
            // IGreeter was generated in this assembly, IClock in the referenced package.
            IGreeter greeter = provider.GetRequiredService<IGreeter>();
            IClock clock = provider.GetRequiredService<IClock>();

            System.Console.WriteLine(greeter.Hello() == "hello" ? clock.Ticks() : 0);
        }
    }
}
