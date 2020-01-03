using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace OsmPipeline
{
	public static partial class Static
	{
		public static IConfigurationRoot Config;
		public static ILoggerFactory LogFactory;
	}

	public class Program
	{
		static void Main(string[] args)
		{
			Static.Config = new ConfigurationBuilder()
				.AddJsonFile("appsettings.json", false, true)
				.Build();
			Environment.CurrentDirectory += Static.Config["WorkingDirectory"];
			IServiceCollection serviceCollection = new ServiceCollection();
			serviceCollection.AddLogging(builder => builder.AddConsole().AddFilter(level => true));
			Static.LogFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();

			try
			{
				var menu = new ConflateMenu();
				menu.Main();
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			Console.ReadKey(true);
		}
	}
}
