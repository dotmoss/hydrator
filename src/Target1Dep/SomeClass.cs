namespace Target1Dep;

public class SomeClass
{
    public int SomeProperty { get; set; }

    public void Display<T>(T message)
    {
        Console.WriteLine(message);
    }
}