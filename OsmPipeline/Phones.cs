using System;
using System.Linq;

namespace OsmPipeline
{
	public static class Phones
	{
		public static string Fix(string number)
		{
			var numbers = new string(number.Where(char.IsDigit).ToArray());

			if (numbers.Length == 11 && numbers.StartsWith("1"))
			{
				return $"+{numbers[0]}-{numbers.Substring(1, 3)}-{numbers.Substring(4, 3)}-{numbers.Substring(7, 4)}";
			}
			else if (numbers.Length == 10)
			{
				return $"+1-{numbers.Substring(0, 3)}-{numbers.Substring(3, 3)}-{numbers.Substring(6, 4)}";
			}
			else
			{
				return "??????????????";
				//throw new Exception($"Too broken to fix: number");
			}
		}
	}
}
