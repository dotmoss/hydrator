namespace Target1.EmptyClasses;

internal class EmptyClass1;

internal class EmptyClass2ButGenericParameter;

internal class EmptyClass3
{
    internal class EmpyClass4Nested;
}

internal class EmptyClass5
{
    internal class NotEmpyClass6Nested
    {
        static NotEmpyClass6Nested()
        {
            Console.WriteLine("6");
        }
    }
}

public class EmptyClass7
{
    internal class NotEmpyClass8Nested
    {
        static NotEmpyClass8Nested()
        {
            Console.WriteLine("6");
        }
    }
}

internal class Generic<T>
{
    public void Method()
    {
        new Generic<EmptyClass2ButGenericParameter>();
    }
}