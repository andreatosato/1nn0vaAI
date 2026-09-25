using Observatory.Api;

if (args.Length > 0 && args[0] == "--serve-fixture")
{
    await HttpFixture.Run(args.Skip(1).ToArray());
    return 0;
}
if (args.Length == 1 && args[0] == "--demo-data")
    return await DemoDataHttpChecks.Run();
if (args.Length == 2 && args[0] == "--unbounded")
    return await ApiSelfCheck.Run(args);
var result = await ApiSelfCheck.Run(args);
if (result != 0) return result;
result = await PromptPreviewHttpChecks.Run(args[0]);
return result == 0 ? await DemoDataHttpChecks.Run() : result;
