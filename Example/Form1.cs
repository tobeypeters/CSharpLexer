#region Copyright notice and license

/*The MIT License(MIT)
Copyright(c), Tobey Peters, https://github.com/tobeypeters

Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
#endregion
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScintillaNET;

namespace Example
{
    public partial class Form1 : Form
    {
        public Form1() => InitializeComponent();

        private async Task<Document> LoadFileAsync(ILoader loader, string path, CancellationToken cancellationToken)
        {
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                using (var reader = new StreamReader(file))
                {
                    var count = 0;
                    var buffer = new char[4096];
                    while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); // Check for cancellation

                        if (!loader.AddData(buffer, count)) { throw new IOException("The data could not be added to the loader."); } // Add the data to the document
                    }

                    return loader.ConvertToDocument();
                }
            }
            catch
            {
                loader.Release();
                throw;
            }
        }

        public static Color IntToColor(int rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        private void Form1_Load(object sender, EventArgs e)
        {
            //For SciLexer.dll, seek out my modified Scintilla source on GitHub. I modified BraceMatch() to ignore Styling.
            //The original Scintilla version of BraceMatch() takes Styling into account. Why?
            //At first, in this project, it looks like it doesn't work.  But, it does.  You have to mess around with it and all the sudden they all properly highlight.
            //I can provide a screenshot of a "real" app, where you can see it works right off the bat.  I'll try to figure out the difference between that project and this project.
            Scintilla.SetModulePath("SciLexer.dll"); //Comment this line out

            sc.CaretStyle = CaretStyle.Block;

            sc.Lexer = Lexer.Container;

            sc.StyleResetDefault();
            sc.Styles[Style.Default].BackColor = IntToColor(0x1E1E1E);
            sc.Styles[Style.Default].ForeColor = IntToColor(0xEFEAEF);
            sc.StyleClearAll();

            //Ugly Test Colors
            sc.Styles[Style.LineNumber].ForeColor = sc.CaretForeColor = IntToColor(0xEFEAEF);
            sc.Styles[CSharpLexer.StyleDefault].ForeColor = IntToColor(0xEFEAEF);
            sc.Styles[CSharpLexer.StyleKeyword].ForeColor = IntToColor(0x35aec6);
            sc.Styles[CSharpLexer.StyleContainerProcedure].ForeColor = Color.HotPink;
            sc.Styles[CSharpLexer.StyleProcedureContainer].ForeColor =
            sc.Styles[CSharpLexer.StyleContextual].ForeColor = IntToColor(0xb4ceaf);
            sc.Styles[CSharpLexer.StyleIdentifier].ForeColor = IntToColor(0xEFEAEF);
            sc.Styles[CSharpLexer.StyleNumber].ForeColor = Color.Purple;
            sc.Styles[CSharpLexer.StyleString].ForeColor = Color.Red;
            sc.Styles[CSharpLexer.StyleComment].ForeColor = Color.Orange;
            sc.Styles[CSharpLexer.StyleProcedure].ForeColor = IntToColor(0x3ac190);
            sc.Styles[CSharpLexer.StyleVerbatim].ForeColor = Color.YellowGreen;
            sc.Styles[CSharpLexer.StylePreprocessor].ForeColor = Color.DarkSlateGray;
            sc.Styles[CSharpLexer.StyleEscapeSequence].ForeColor = Color.Yellow;
            sc.Styles[CSharpLexer.StyleOperator].ForeColor = Color.HotPink;
            sc.Styles[CSharpLexer.StyleBraces].ForeColor = Color.GreenYellow;
            sc.Styles[CSharpLexer.StyleError].ForeColor = Color.DarkRed;
            sc.Styles[CSharpLexer.StyleUser].ForeColor = Color.Olive;
            sc.Styles[CSharpLexer.StyleMultiIdentifier].ForeColor = Color.DeepPink;

            CSharpLexer.Init_Lexer(sc);
            CSharpLexer.SetKeyWords("abstract add as ascending async await base bool break by byte case catch char checked class const continue decimal default delegate descending do double dynamic else enum equals explicit extern false finally fixed float for foreach from get global global goto goto group if implicit in int interface internal into is join let lock long namespace new null object on operator orderby out override params partial private protected public readonly ref remove return sbyte sealed select set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using value var virtual void volatile where while yield",
                                    inUserKeywords: "Goblin Hammer", inMultiStringKeywords: "New York,New Jersey", AutoFillContextual: true
                                   );

            sc.UpdateUI += (s, ue) =>
            {
                label3.Text = $"{sc.CurrentLine + 1}";
                label4.Text = $"{sc.CurrentPosition + 1}";
                label5.Text = $"{(sc.Overtype ? "OVR" : "INS")}";
            };
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (od.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var loader = sc.CreateLoader(256);
                    if (loader == null)
                        throw new ApplicationException("Unable to create loader.");

                    var cts = new CancellationTokenSource();
                    var document = await LoadFileAsync(loader, od.FileName, cts.Token);
                    sc.Document = document;

                    sc.ReleaseDocument(document);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                    MessageBox.Show(this, "There was an error loading the file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}