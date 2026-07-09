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
        Console.WriteLine(entity.Method1());
        Console.WriteLine(entity.Method2());
        Console.WriteLine(entity.Property1);
        Console.WriteLine(entity.Property2);
        Console.WriteLine(entity.Field1);
        Console.WriteLine(entity.Field2);
        Console.WriteLine(new SomeClass().SomeProperty);
        Console.WriteLine(Entity<Program>.StaticField1);
        new SomeClass().Display("message");
        new B().a();
        Console.ReadLine();
    }
}

public class A
{
    internal virtual void a()
    {
        Console.WriteLine("a");
    }
}

public class B : A
{
    internal override void a()
    {
        base.a();
        Console.WriteLine("b");
    }
}

public class Entity<T>
{
    void OnInitialized()
    {
        Console.WriteLine("Initialized!");
    }

    public int Method1()
    {
        Console.WriteLine("Method1!");
        return 1;
    }

    public long Method2()
    {
        Console.WriteLine("Method2!");
        return 2;
    }

    public int Property1 => 1;

    public long Property2 => 2;

    public int Field1 = 1;

    public long Field2 = 2;

    public static int StaticField1 = 3;

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