namespace EnginePrePatcher;

class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Wrong args!");
            return 1;
        }

        Console.WriteLine("Start patching!");

        return AssemblyPatcher.Process(args[0]) ? 0 : 1;
    }
}
