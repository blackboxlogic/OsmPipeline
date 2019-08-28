using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SharpKml.Dom;
using SharpKml.Engine;

namespace OsmPipeline.Fittings
{
	public class KMLFileSource : ISource<Element>, IDisposable
	{
		private string FileName;
		private Stream FileStream;

		public KMLFileSource(string fileName)
		{
			FileName = fileName;
		}

		public IEnumerator<Element> GetEnumerator()
		{
			// maybe don't hold the stream open?
			FileStream = FileStream ?? File.Open(FileName, FileMode.Open);
			return KmlFile.Load(FileStream).Root.Children.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			FileStream = FileStream ?? File.Open(FileName, FileMode.Open);
			return KmlFile.Load(FileStream).Root.Children.GetEnumerator();
		}

		public void Dispose()
		{
			var fileStream = FileStream;
			if (fileStream != null)
			{
				fileStream.Dispose();
				FileStream = null;
			}
		}
	}
}
