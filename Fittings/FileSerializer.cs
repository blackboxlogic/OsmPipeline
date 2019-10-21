using System;
using System.IO;
using System.Xml.Serialization;

namespace OsmPipeline.Fittings
{
	public static class FileSerializer
	{
		public static T ReadCacheOrSource<T>(string fileName, Func<T> source) where T : class, IXmlSerializable
		{
			return Read<T>(fileName) ?? Write<T>(fileName, source());
		}

		public static T Read<T>(string fileName) where T : class, IXmlSerializable
		{
			XmlSerializer Serializer = new XmlSerializer(typeof(T));

			if (File.Exists(fileName))
			{
				using (var fileStream = File.OpenRead(fileName))
				{
					var element = (T)Serializer.Deserialize(fileStream);
					return element;
				}
			}

			return null;
		}

		public static T Write<T>(string fileName, T element) where T : class, IXmlSerializable
		{
			XmlSerializer Serializer = new XmlSerializer(typeof(T));

			using (var fileStream = new StreamWriter(fileName, false))
			{
				Serializer.Serialize(fileStream, element);
			}

			return element;
		}
	}
}
