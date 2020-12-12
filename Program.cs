using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace sorteraochfixa
{
	class Program
	{
		static void Main(string[] args)
		{
			var timer = new Stopwatch();
			timer.Start();
			var src = args.Length > 0 ? args[0] : "O:/Old software/Amiga/TOSEC 2016-11-11/Commodore Amiga - Demos - Animations and Videos";
			var dest = args.Length > 1 ? args[1] : "./files";
			var noSplit = args.Any(x => x.Equals("--nosplit", StringComparison.InvariantCultureIgnoreCase));
			var unpack = args.Any(x => x.Equals("--unpack", StringComparison.InvariantCultureIgnoreCase));
			var useFilter = args.Any(x => x.Equals("--filter", StringComparison.InvariantCultureIgnoreCase));
			var dryrun = args.Any(x => x.Equals("--dryrun", StringComparison.InvariantCultureIgnoreCase));

			List<FileInfo> files = null;
			AnsiConsole.Status()
				.Start($"Reading files from [bold]{src.Replace("[", "[[").Replace("]", "]]")}[/]", ctx => {
					ctx.Spinner(Spinner.Known.Dots);
					var srcDir = new DirectoryInfo(src);
					files = srcDir
							.GetFiles()
							.Where(f => !useFilter || !Filter.IsMatch(f.Name))
							.OrderBy(x => x.Name)
							.ToList();
				});

			AnsiConsole.MarkupLine($"Found [green]{files.Count}[/] files in [bold]{src.Replace("[", "[[").Replace("]", "]]")}[/].");

			var processed = 0;
			var duplicates = 0;
			var useSubFolders = !noSplit && files.Count > 200;

			AnsiConsole.Progress()
				.AutoClear(false)
				.Columns(new ProgressColumn[] {
					new TaskDescriptionColumn(),    // Task description
					new ProgressBarColumn(),        // Progress bar
					new PercentageColumn(),         // Percentage
					new RemainingTimeColumn(),      // Remaining time
					new SpinnerColumn(),            // Spinner
				})
				.Start(ctx => {
					var task1 = ctx.AddTask("Removing duplicates");
					var task3 = ctx.AddTask($"Copying files to [bold]{dest}[/]", new ProgressTaskSettings { AutoStart = false });

					var uniqueFiles = new ConcurrentBag<FileInfo>();

					Parallel.For(0, files.Count, (index, state) =>
					{
						//var pct = index / files.Count * 100
						//Console.Write(file.Name);
						var file = files[index];
						var relevantName = RelevantName(file.Name);
						var isDuplicate = files.Take(index).Any(x => RelevantName(x.Name).Equals(relevantName, StringComparison.InvariantCultureIgnoreCase));
						if (isDuplicate) {
							//Console.Write(" (duplicate)");
							duplicates++;
						}
						else
							uniqueFiles.Add(file);

						Interlocked.Increment(ref processed);

						//Update progress -- not worth locking for
						var pct = processed*100/files.Count;
						var diff = pct - task1.Percentage;
						if (diff > 0)
							task1.Increment(diff);
					});

					if (task1.Percentage < 100)
					{
						task1.Increment(100-task1.Percentage);
						task1.StopTask();
					}

					processed = 0; //Starta om räknaren
					task3.Description = $"{(unpack ? "Unpacking" : "Copying")} {uniqueFiles.Count-processed} files to [bold]{dest}[/]";
					task3.StartTask();
					foreach(var file in uniqueFiles) {
						//TODO: Gruppa 0-9 som 9
						var destination = dest;
						if (useSubFolders) {
							var group = Words.Match(file.Name).Value.ToUpperInvariant();
							if (Digits.IsMatch(group))
								group = "0";
							destination = Path.Combine(dest, group);
						}

						if (!dryrun) 
						{
							if (!Directory.Exists(destination))
								Directory.CreateDirectory(destination);

							if (!unpack)
								file.CopyTo(Path.Combine(destination, file.Name), true);
							else
								ZipFile.ExtractToDirectory(file.FullName, destination, true);
						}


						Interlocked.Increment(ref processed);
						var pct = processed*100/uniqueFiles.Count;
						var diff = pct - task3.Percentage;
						if (diff > 0)
							task3.Increment(diff);
					}

				});

			timer.Stop();

			AnsiConsole.MarkupLine($"Found [red]{duplicates}[/] duplicates in [green]{files.Count}[/] files");
			AnsiConsole.MarkupLine($"Time elapsed: [bold]{timer.Elapsed} [/]seconds");
		}

		static Regex Digits = new Regex("\\d", RegexOptions.Compiled);
		static Regex Words = new Regex("\\w", RegexOptions.Compiled);
		static Regex Filter = new Regex(@"\(AGA\)|\(FR\)|\(DE\)|\(ES\)|\(PL\)|\(IT\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		static string RelevantName(string filename) => filename.Substring(0, filename.LastIndexOf(")"));
	}
}
