using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
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
			var includeFolders = args.Any(x => x.Equals("--folders", StringComparison.InvariantCultureIgnoreCase));
			var flattenSubdirs = args.Any(x => x.Equals("--flatten", StringComparison.InvariantCultureIgnoreCase));

			List<FileSystemInfo> fsItems = null;
			// List<DirectoryInfo> dirs = null;
			AnsiConsole.Status()
				.Start($"Reading files from [bold]{src.Replace("[", "[[").Replace("]", "]]")}[/]", ctx => {
					ctx.Spinner(Spinner.Known.Dots);
					var srcDir = new DirectoryInfo(src);
					if (!includeFolders)
						fsItems = srcDir
								.GetFiles()
								.Select(x => x as FileSystemInfo)
								.Where(f => !useFilter || !Filter.IsMatch(f.Name))
								.OrderBy(x => x.Name)
								.ToList();
					else
						fsItems = srcDir
								.GetFileSystemInfos()
								.Select(x => x as FileSystemInfo)
								.Where(f => !useFilter || !Filter.IsMatch(f.Name))
								.OrderBy(x => x.Name)
								.ToList();
				});

			AnsiConsole.MarkupLine($"Found [green]{fsItems.Count}[/] files in [bold]{src.Replace("[", "[[").Replace("]", "]]")}[/].");

			var duplicates = 0;
			var useSubFolders = !noSplit && fsItems.Count > 200;

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
					var task1 = ctx.AddTask("Removing duplicates", new ProgressTaskSettings { MaxValue = fsItems.Count });

					var uniqueFiles = new ConcurrentBag<FileSystemInfo>();

					Parallel.ForEach(fsItems.GroupBy(x => x.Name[0]), (itemsByFirstLetter, _, _) => {
						Parallel.ForEach(itemsByFirstLetter, (file, state, index) =>
						{
							var relevantName = RelevantName(file.Name);
							var isDuplicate = itemsByFirstLetter.Take((int)index).Any(x => RelevantName(x.Name).Equals(relevantName, StringComparison.InvariantCultureIgnoreCase));
							if (isDuplicate) {
								duplicates++;
							}
							else
								uniqueFiles.Add(file);

							task1.Increment(1);
						});
					});

					task1.StopTask();

					var task3 = ctx.AddTask($"Copying files to [bold]{dest}[/]", new ProgressTaskSettings { AutoStart = false, MaxValue = uniqueFiles.Count });
					task3.Description = $"{(unpack ? "Unpacking" : "Copying")} {uniqueFiles.Count} files to [bold]{dest}[/]";
					task3.StartTask();
					foreach(var file in uniqueFiles) {
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

							if (!unpack) {
								if (file is FileInfo f)
									f.CopyTo(Path.Combine(destination, file.Name), true);
								else if (file is DirectoryInfo d) {
									if (!flattenSubdirs)
										destination = Directory.CreateDirectory(Path.Combine(destination, d.Name)).FullName;
									foreach(var fileInDir in d.GetFiles())
										fileInDir.CopyTo(Path.Combine(destination, fileInDir.Name));
								}
							}
							else
								ZipFile.ExtractToDirectory(file.FullName, destination, true);
						}

						task3.Increment(1);
					}

				});

			timer.Stop();

			AnsiConsole.MarkupLine($"Found [red]{duplicates}[/] duplicates in [green]{fsItems.Count}[/] files");
			AnsiConsole.MarkupLine($"Time elapsed: [bold]{timer.Elapsed} [/]seconds");
		}

		static Regex Digits = new Regex("\\d", RegexOptions.Compiled);
		static Regex Words = new Regex("\\w", RegexOptions.Compiled);
		static Regex Filter = new Regex(@"\(AGA\)|\(FR\)|\(DE\)|\(ES\)|\(PL\)|\(IT\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		static string RelevantName(string filename) => filename.Substring(0, filename.LastIndexOf(")"));
	}
}
