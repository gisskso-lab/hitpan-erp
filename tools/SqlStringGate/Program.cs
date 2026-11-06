using System.Text;
using SqlStringGate;

Console.OutputEncoding = new UTF8Encoding(false);
return GateRunner.Run(Path.GetFullPath(args.Length > 0 ? args[0] : "."), Console.Out);
