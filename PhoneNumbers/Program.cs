using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OsmPipeline.PhoneNumbers
{
	public static partial class Static
	{
		public static IConfigurationRoot Config;
		public static ILoggerFactory LogFactory;
	}

	class Program
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
				Phones.FixPhones(Static.LogFactory, Static.Config, null);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			Console.ReadKey(true);
		}
	}
}
