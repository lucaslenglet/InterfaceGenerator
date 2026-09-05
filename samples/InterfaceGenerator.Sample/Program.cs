using System;
using Microsoft.Extensions.DependencyInjection;

namespace InterfaceGenerator.Sample;

public static class Program
{
    public static void Main()
    {
        // AddInterfaceGeneratorSampleServices is generated: named after the assembly, and holding
        // one registration per annotated type. Scoped is the default for the types that did not
        // pick a lifetime; Greeter asked for Singleton and keeps it.
        ServiceProvider provider = new ServiceCollection()
            .AddInterfaceGeneratorSampleServices(ServiceLifetime.Scoped)
            .BuildServiceProvider();

        using (provider)
        {
            // IGreeter and IClock are generated, and the classes implement them without saying so.
            IGreeter greeter = provider.GetRequiredService<IGreeter>();
            IClock clock = provider.GetRequiredService<IClock>();

            Console.WriteLine(greeter.Hello("World"));
            Console.WriteLine(clock.Now().Year);

            // greeter.DumpState() would not compile: it is excluded from the interface.
            ((Greeter)greeter).DumpState();
        }
    }
}
