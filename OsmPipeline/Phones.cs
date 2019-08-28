using System;
using System.Linq;

namespace OsmPipeline
{
	public static class Phones
	{
		public static string Correct = @"^\\+1\\-[0-9]{3}\\-[0-9]{3}\\-[0-9]{4}$";
		public static string ElevenStartsWithOne = "^([^0-9]*1)([^0-9]*[0-9]){10}[^0-9]*$";
		public static string TenStartsWith207 = "^([^0-9]*2)([^0-9]*0)([^0-9]*7)([^0-9]*[0-9]){7}[^0-9]*$";
		public static string TenStartsWith800 = "^([^0-9]*8)([^0-9]*0)([^0-9]*0)([^0-9]*[0-9]){7}[^0-9]*$";
		public static string TenStartsWith888 = "^([^0-9]*8)([^0-9]*8)([^0-9]*8)([^0-9]*[0-9]){7}[^0-9]*$";
		public static string HasLetters = "[^a-zA-Z]";
		public static string HasAnything = ".*";

		public static bool TryFix(string number, string[] safeAreas, string assumedArea, out string fixedNumber)
		{
			if (number.Any(char.IsLetter))
			{
				fixedNumber = null;
				return false;
			}

			var numbers = new string(number.Where(char.IsDigit).ToArray());

			if (numbers.Length == 11 && numbers.StartsWith("1"))
			{
				fixedNumber = $"+1-{numbers.Substring(1, 3)}-{numbers.Substring(4, 3)}-{numbers.Substring(7, 4)}";
				return true;
			}
			else if (numbers.Length == 10 && safeAreas.Contains(numbers.Substring(0, 3)))
			{
				fixedNumber = $"+1-{numbers.Substring(0, 3)}-{numbers.Substring(3, 3)}-{numbers.Substring(6, 4)}";
				return true;
			}
			else if (numbers.Length == 7 && assumedArea != null)
			{
				fixedNumber = $"+1-{assumedArea}-{numbers.Substring(0, 3)}-{numbers.Substring(3, 4)}";
				return true;
			}
			else
			{
				fixedNumber = null;
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
