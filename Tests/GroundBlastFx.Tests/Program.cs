using System;
using System.Reflection;

namespace GroundBlastFx.Tests
{
    /// <summary>Marque une méthode statique publique sans paramètre comme test.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }

        public static void Near(double expected, double actual, double tolerance, string message)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new AssertionException(message + " — attendu " + expected.ToString("G6") + " ± " + tolerance.ToString("G3") + ", obtenu " + actual.ToString("G6"));
        }

        public static void Equal(long expected, long actual, string message)
        {
            if (expected != actual) throw new AssertionException(message + " — attendu " + expected + ", obtenu " + actual);
        }
    }

    public static class Program
    {
        public static int Main()
        {
            int passed = 0, failed = 0;
            foreach (Type type in typeof(Program).Assembly.GetTypes())
            {
                foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.GetCustomAttribute<TestAttribute>() == null) continue;
                    string name = type.Name + "." + m.Name;
                    try
                    {
                        m.Invoke(null, null);
                        passed++;
                        Console.WriteLine("  OK    " + name);
                    }
                    catch (TargetInvocationException e)
                    {
                        failed++;
                        Console.WriteLine("  ÉCHEC " + name + " : " + (e.InnerException?.Message ?? e.Message));
                    }
                }
            }
            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "Tous les tests passent (" + passed + ")."
                : failed + " test(s) en échec, " + passed + " réussi(s).");
            return failed == 0 ? 0 : 1;
        }
    }
}
