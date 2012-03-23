using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SquishIt.Framework.Base;
using SquishIt.Framework.Minifiers;
using SquishIt.Framework.Minifiers.CSS;
using SquishIt.Framework.Resolvers;
using SquishIt.Framework.Files;
using SquishIt.Framework.Utilities;

namespace SquishIt.Framework.Css
{
    public class CSSBundle : BundleBase<CSSBundle>
    {
        private readonly static Regex IMPORT_PATTERN = new Regex(@"@import +url\(([""']){0,1}(.*?)\1{0,1}\);", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private const string CSS_TEMPLATE = "<link rel=\"stylesheet\" type=\"text/css\" {0}href=\"{1}\" />";
        private const string CACHE_PREFIX = "css";
        private const string TAG_FORMAT = "<style type=\"text/css\">{0}</style>";

        private bool ShouldImport { get; set; }
        private bool ShouldAppendHashForAssets { get; set; }

        
        protected override string Template
        {
            get { return CSS_TEMPLATE; }
        }

        protected override string CachePrefix
        {
            get { return CACHE_PREFIX; }
        }

        protected override IMinifier<CSSBundle> DefaultMinifier
        {
            get { return new MsCompressor(); }
        }

        protected override IEnumerable<string> allowedExtensions 
        {
            get { return instanceAllowedExtensions.Union(Bundle.AllowedGlobalExtensions.Union(Bundle.AllowedStyleExtensions)); }
        }

        protected override IEnumerable<string> disallowedExtensions 
        {
            get { return Bundle.AllowedScriptExtensions; }
        }

        protected override string tagFormat
        {
            get { return typeless ? TAG_FORMAT.Replace(" type=\"text/css\"", "") : TAG_FORMAT; }
        }

        public CSSBundle()
            : base(new FileWriterFactory(new RetryableFileOpener(), 5), new FileReaderFactory(new RetryableFileOpener(), 5), new DebugStatusReader(), new CurrentDirectoryWrapper(), new Hasher(new RetryableFileOpener()), new BundleCache())
        {
        }

        public CSSBundle(IDebugStatusReader debugStatusReader)
            : base(new FileWriterFactory(new RetryableFileOpener(), 5), new FileReaderFactory(new RetryableFileOpener(), 5), debugStatusReader, new CurrentDirectoryWrapper(), new Hasher(new RetryableFileOpener()), new BundleCache())
        {
        }

        public CSSBundle(IDebugStatusReader debugStatusReader, IFileWriterFactory fileWriterFactory, IFileReaderFactory fileReaderFactory, ICurrentDirectoryWrapper currentDirectoryWrapper, IHasher hasher, IBundleCache bundleCache)
            : base(fileWriterFactory, fileReaderFactory, debugStatusReader, currentDirectoryWrapper, hasher, bundleCache)
        {
        }

        private string ProcessImport(string file, string outputFile, string css)
        {
            var sourcePath = FileSystem.ResolveFileSystemPathToAppRelative(Path.GetDirectoryName(file)) + "/";

            return IMPORT_PATTERN.Replace(css, match =>
            {
                var importPath = match.Groups[2].Value;
                string import;
                if (importPath.StartsWith("/"))
                {
                    import = FileSystem.ResolveAppRelativePathToFileSystem(importPath);
                }
                else
                {
                    import = FileSystem.ResolveAppRelativePathToFileSystem(sourcePath + importPath);
                }
                DependentFiles.Add(import);
                return ProcessCssFile(import, outputFile, true);
            });
        }

        public CSSBundle ProcessImports()
        {
            ShouldImport = true;
            return this;
        }

        public CSSBundle AppendHashForAssets()
        {
            ShouldAppendHashForAssets = true;
            return this;
        }

        protected override string BeforeMinify(string outputFile, List<string> filePaths, IEnumerable<string> arbitraryContent)
        {
            var outputCss = new StringBuilder();

            filePaths.Select(file => ProcessCssFile (file, outputFile))
                .Concat(arbitraryContent)
                .Aggregate(outputCss, (builder, val) => builder.Append(val + "\n"));

            return outputCss.ToString();
        }

        private string PreprocessCssFile(string file, IEnumerable<IPreprocessor> preprocessors)
        {
            lock (typeof(CSSBundle))
            {
                return PreprocessFile(file, preprocessors);
            }
        }

        string ProcessCssFile(string file, string outputFile, bool asImport = false) {
            string css = null;

            var preprocessors = FindPreprocessors(file);

            if(preprocessors != null)
            {
                css = PreprocessCssFile(file, preprocessors);
            }
            else
            {
                css = ReadFile(file);
            }

            if (ShouldImport)
            {
                css = ProcessImport(file, outputFile, css);
            }

            ICssAssetsFileHasher fileHasher = null;

            if (ShouldAppendHashForAssets)
            {
                var fileResolver = new FileSystemResolver();
                fileHasher = new CssAssetsFileHasher(HashKeyName, fileResolver, hasher);
            }

            return CSSPathRewriter.RewriteCssPaths(outputFile, file, css, fileHasher, asImport);
        }

        internal override void BeforeRenderDebug()
        {
            foreach (var asset in bundleState.Assets)
            {
                var localPath = asset.LocalPath;
                var preprocessors = FindPreprocessors(localPath);
                if (preprocessors != null && preprocessors.Count() > 0)
                {
                    string outputFile = FileSystem.ResolveAppRelativePathToFileSystem(localPath);

                    string css = PreprocessCssFile(outputFile, preprocessors);
                    outputFile += ".debug.css";
                    using (var fileWriter = fileWriterFactory.GetFileWriter(outputFile))
                    {
                        fileWriter.Write(css);
                    }
                    asset.LocalPath = localPath + ".debug.css";
                }
            }
        }
    }
}