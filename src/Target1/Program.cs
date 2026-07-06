using System.Runtime.CompilerServices;
using Target1Dep;

namespace Target1;

public class Program
{
    public class Program1;
    public class Program2;

    static void Main(string[] args)
    {
        var entity = new Entity<Program>();
        Entity<Program>.Dispatcher.OnInitialized(entity);
        Console.WriteLine(new OuterClass7<Program>.Inner8().Data == null);
        Console.WriteLine(new OuterClass7<Program>.Inner9().Data.GetType());
        Console.WriteLine(new Class10<Program>.Class11<Program1>.Class12<Program2>().GetType());
        new SomeClass().Display("message");
        Console.ReadLine();
    }
}

public class Entity<T>
{
    void OnInitialized()
    {
        Console.WriteLine("Initialized!");
    }

    public static class Dispatcher
    {
        public static void OnInitialized(Entity<T> entity) => entity.OnInitialized();
    }
}

public class OuterClass7<T>
{
    public class Inner8
    {
        public T Data { get; set; } = default;
    }

    public class Inner9
    {
        public static int counter = 0;

        public int Data { get; set; }
    }
}

public class Class10<T1>
{
    public class Class11<[MyCustom] T2>
    {
        public class Class12<T3>
        {

        }
    }
}

[AttributeUsage(AttributeTargets.GenericParameter)]
public class MyCustomAttribute : Attribute;