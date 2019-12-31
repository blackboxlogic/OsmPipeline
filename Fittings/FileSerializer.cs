using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace OsmPipeline.Fittings
{
	public static class FileSerializer
	{
		public static async Task<T> ReadXmlCacheOrSource<T>(string fileName, Func<Task<T>> source) where T : class, IXmlSerializable
		{
			return ReadXml<T>(fileName) ?? WriteXml(fileName, await source());
		}

		public static T ReadXmlCacheOrSource<T>(string fileName, Func<T> source) where T : class, IXmlSerializable
		{
			return ReadXml<T>(fileName) ?? WriteXml(fileName, source());
		}

		public static async Task<T> ReadJsonCacheOrSource<T>(string fileName, Func<Task<T>> source) where T : class
		{
			return ReadJson<T>(fileName) ?? WriteJson(fileName, await source());
		}

		public static T ReadJsonCacheOrSource<T>(string fileName, Func<T> source) where T : class
		{
			return ReadJson<T>(fileName) ?? WriteJson(fileName, source());
		}

		public static T ReadXml<T>(string fileName) where T : class, IXmlSerializable
		{
			if (File.Exists(fileName))
			{
				using (var fileStream = File.OpenRead(fileName))
				{
					XmlSerializer Serializer = new XmlSerializer(typeof(T));
					var element = (T)Serializer.Deserialize(fileStream);
					return element;
				}
			}

			return null;
		}

		public static T WriteXml<T>(string fileName, T element) where T : IXmlSerializable
		{
			var directory = Path.GetDirectoryName(fileName);
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
			using (var fileStream = new StreamWriter(fileName, false))
			{
				XmlSerializer Serializer = new XmlSerializer(typeof(T));
				Serializer.Serialize(fileStream, element);
			}

			return element;
		}

		// Should be async
		public static T ReadJson<T>(string fileName) where T : class
		{
			if (File.Exists(fileName))
			{
				var serializer = new JsonSerializer();
				using (var sr = new StreamReader(fileName))
				using (var jsonTextReader = new JsonTextReader(sr))
				{
					return serializer.Deserialize<T>(jsonTextReader);
				}
			}

			return null;
		}

		// Should be async
		public static T WriteJson<T>(string fileName, T element)
		{
			var directory = Path.GetDirectoryName(fileName);
			if (!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);
			var serializer = new JsonSerializer() { Formatting = Formatting.Indented };
			using (var sw = new StreamWriter(fileName))
			using (var jsonTextWriter = new JsonTextWriter(sw) { IndentChar = '	', Indentation = 1 } )
			{
				serializer.Serialize(jsonTextWriter, element);
			}

			return element;
		}
	}
}
