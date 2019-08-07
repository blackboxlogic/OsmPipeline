using System;
using System.Linq;

namespace OsmPipeline
{
	public static class Phones
	{
		public static bool TryFix(string number, string safeAreaCode, out string fixedNumber)
		{
			var numbers = new string(number.Where(char.IsDigit).ToArray());

			if (numbers.Length == 11 && numbers.StartsWith("1"))
			{
				fixedNumber = $"+{numbers[0]}-{numbers.Substring(1, 3)}-{numbers.Substring(4, 3)}-{numbers.Substring(7, 4)}";
				return true;
			}
			else if (numbers.Length == 10 && numbers.Substring(0, 3) == safeAreaCode)
			{
				fixedNumber = $"+1-{numbers.Substring(0, 3)}-{numbers.Substring(3, 3)}-{numbers.Substring(6, 4)}";
				return true;
			}
			else if (numbers.Length == 7)
			{
				fixedNumber = $"+1-{safeAreaCode}-{numbers.Substring(3, 3)}-{numbers.Substring(6, 4)}";
				return true;
			}
			else
			{
				fixedNumber = null;
				Console.WriteLine($"Couldn't fix {number}");
				return false;
			}
		}

		public static bool TryFix(string number, out string fixedNumber)
		{
			var numbers = new string(number.Where(char.IsDigit).ToArray());

			if (numbers.Length == 11 && numbers.StartsWith("1"))
			{
				fixedNumber = $"+{numbers[0]}-{numbers.Substring(1, 3)}-{numbers.Substring(4, 3)}-{numbers.Substring(7, 4)}";
				return true;
			}
			else
			{
				fixedNumber = null;
				Console.WriteLine($"Couldn't fix {number}");
				return false;
			}
		}
	}
}
