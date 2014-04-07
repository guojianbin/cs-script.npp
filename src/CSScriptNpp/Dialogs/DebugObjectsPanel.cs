﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using CSScriptIntellisense;

namespace CSScriptNpp.Dialogs
{
    public partial class DebugObjectsPanel : Form
    {
        private ListViewItem focucedItem;

        public DebugObjectsPanel()
        {
            InitializeComponent();
            InitInPlaceEditor();
        }

        private int selectedSubItem = 0;

        void listView1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // Check the subitem clicked .
            int spos = 0;

            for (int i = 0; i < listView1.Columns.Count; i++)
            {
                int epos = spos + listView1.Columns[i].Width;

                if (spos < e.X && e.X < epos)
                {
                    selectedSubItem = i;
                    break;
                }

                spos = epos;
            }

            //currently allow editing name only
            if (selectedSubItem != 0) //currently allow editing name only
                return;

            //changing the name of the item allows only fro the root DbgObject
            if (selectedSubItem == 0 && (focucedItem.Tag as DbgObject).Parent != null)
                return;

            int xOffset = 2;

            editBox.Size = new Size(listView1.Columns[selectedSubItem].Width - xOffset, focucedItem.Bounds.Bottom - focucedItem.Bounds.Top);
            editBox.Location = new Point(spos + xOffset, focucedItem.Bounds.Y);
            editBox.IsEditing = true;
            editBox.Show();
            editBox.Text = focucedItem.SubItems[selectedSubItem].Text;
            editBox.SelectAll();
            editBox.Focus();
        }

        void editBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Return)
                EndEditing();

            else if (e.KeyData == Keys.Escape)
                editBox.Hide();
        }

        public delegate void OnEditCellCompleteHandler(int column, string oldValue, string newValue);

        public event OnEditCellCompleteHandler OnEditCellComplete;

        private void FocusOver(object sender, System.EventArgs e)
        {
            EndEditing();
        }

        void EndEditing()
        {
            if (editBox.IsEditing)
            {
                string oldValue;
                string newValue = editBox.Text;
                editBox.IsEditing = false;
                if (focucedItem.Tag != AddNewPlaceholder)
                {
                    oldValue = focucedItem.SubItems[selectedSubItem].Text;
                    focucedItem.SubItems[selectedSubItem].Text = newValue;
                    if (selectedSubItem == 0 && newValue == "")
                    {
                        listView1.Items.Remove(focucedItem);
                        focucedItem = null;
                    }
                }
                else
                {
                    oldValue = null;
                    AddWatchExpression(newValue);
                }
                editBox.Hide();

                if (OnEditCellComplete != null)
                    OnEditCellComplete(selectedSubItem, oldValue, newValue);
            }
        }

        public void ClearWatchExpressions()
        {
            listView1.Items.Clear();
            Debugger.RemoveAllWatch();
        }

        public void AddWatchExpression(string expression)
        {
            expression = expression.Trim();
            if (!string.IsNullOrEmpty(expression))
            {
                AddWatchObject(new DbgObject
                    {
                        DbgId = "",
                        Name = expression,
                        IsExpression = true,
                        Value = "<N/A>",
                        Type = "<N/A>",
                    });

                Debugger.AddWatch(expression);
            }
        }

        public void SetData(string data)
        {
            ResetWatchObjects(ToWatchObjects(data));
        }

        public void UpdateData(string data)
        {
            DbgObject[] freshObjects = ToWatchObjects(data);

            var nonRootItems = new List<ListViewItem>();

            bool updated = false;

            foreach (ListViewItem item in listView1.Items)
            {
                var itemObject = item.Tag as DbgObject;
                if (itemObject != null)
                {
                    itemObject.IsModified = false;
                    if (itemObject.Parent != null)
                    {
                        nonRootItems.Add(item);
                    }
                    else
                    {
                        DbgObject update = freshObjects.Where(x => x.Name == itemObject.Name).FirstOrDefault();
                        if (update != null)
                        {
                            itemObject.CopyDbgDataFrom(update);
                            itemObject.IsModified = (item.SubItems[1].Text != update.Value);
                            item.SubItems[1].Text = update.Value;
                            item.SubItems[2].Text = update.Type;
                            updated = true;
                        }
                    }
                }
            }

            nonRootItems.ForEach(x =>
               listView1.Items.Remove(x));

            if (updated)
                listView1.Invalidate();
        }

        DbgObject[] ToWatchObjects(string data)
        {
            if (string.IsNullOrEmpty(data))
                return new DbgObject[0];

            var root = XElement.Parse(data);

            var values = root.Elements().Select(dbgValue =>
            {
                string valName = dbgValue.Attribute("name").Value;

                if (valName.EndsWith("__BackingField")) //ignore auto-property backing fields
                    return null;


                Func<string, bool> getBoolAttribute = attrName => dbgValue.Attribute(attrName).Value == "true";

                var dbgObject = new DbgObject();
                dbgObject.DbgId = dbgValue.Attribute("id").Value;
                dbgObject.Name = valName;
                dbgObject.Type = dbgValue.Attribute("typeName").Value.ReplaceClrAliaces();
                dbgObject.IsArray = getBoolAttribute("isArray");
                dbgObject.HasChildren = getBoolAttribute("isComplex");
                dbgObject.IsField = !getBoolAttribute("isProperty");
                dbgObject.IsStatic = getBoolAttribute("isStatic");

                if (!dbgObject.HasChildren)
                {
                    // This is a catch-all for primitives.
                    string stValue = dbgValue.Attribute("value").Value;
                    dbgObject.Value = stValue;
                }

                return dbgObject;

            }).Where(x => x != null);

            var staticMembers = values.Where(x => x.IsStatic);
            var instanceMembers = values.Where(x => !x.IsStatic);
            var result = new List<DbgObject>(instanceMembers);
            if (staticMembers.Any())
                result.Add(
                    new DbgObject
                    {
                        Name = "Static members",
                        HasChildren = true,
                        IsSeparator = true,
                        Children = staticMembers.ToArray()
                    });
            return result.ToArray();
        }

        protected void AddWatchObject(DbgObject item)
        {
            int insertionPosition = listView1.Items.Count;

            if (listView1.Items.Count > 0 && listView1.Items[listView1.Items.Count - 1].Tag == AddNewPlaceholder)
                insertionPosition--;
            listView1.Items.Insert(insertionPosition, item.ToListViewItem());
        }

        protected void ResetWatchObjects(params DbgObject[] items)
        {
            listView1.Items.Clear();

            foreach (var item in items.ToListViewItems())
                listView1.Items.Add(item);

            if (!IsReadOnly)
                listView1.Items.Add(AddNewPlaceholder.ToListViewItem());
        }

        DbgObject AddNewPlaceholder = new DbgObject { DbgId = "AddNewPlaceholder", Name = "" };

        void InsertWatchObjects(int index, params DbgObject[] items)
        {
            if (index == listView1.Items.Count)
                listView1.Items.AddRange(items.ToListViewItems().ToArray());
            else
                foreach (var item in items.ToListViewItems().Reverse())
                    listView1.Items.Insert(index, item);
        }

        int triangleMargin = 8;
        int xMargin = 27; //fixed; to accommodate the icon

        Range GetItemExpenderClickableRange(ListViewItem item)
        {
            var dbgObject = (DbgObject)item.Tag;

            int xOffset = 10 * (dbgObject.IndentationLevel); //depends on the indentation
            return new Range { Start = xOffset, End = xOffset + triangleMargin };
        }

        private void listView1_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            e.DrawBackground();

            var dbgObject = (DbgObject)e.Item.Tag;

            if (dbgObject == AddNewPlaceholder)
                return;

            var textBrush = Brushes.Black;

            if (e.ColumnIndex == 1 && dbgObject.IsModified) //'Value' has changed
                textBrush = Brushes.Red;

            if (!Debugger.IsInBreak)
                textBrush = Brushes.DarkGray;

            if (e.ColumnIndex == 0)
            {

                var clickableRange = GetItemExpenderClickableRange(e.Item);

                int X = e.Bounds.X + clickableRange.Start;
                int Y = e.Bounds.Y;

                var range = GetItemExpenderClickableRange(e.Item);

                Image icon;

                if (dbgObject.IsUnresolved)
                    icon = Resources.Resources.unresolved_value;
                else if (dbgObject.IsSeparator)
                    icon = Resources.Resources.dbg_container;
                else if (dbgObject.IsField)
                    icon = Resources.Resources.field;
                else
                    icon = Resources.Resources.property;

                e.Graphics.DrawImage(icon, range.End + 6, e.Bounds.Y);

                if (dbgObject.HasChildren)
                {
                    int xOffset;
                    int triangleWidth;
                    int triangleHeight;
                    int yOffset;

                    if (dbgObject.IsExpanded)
                    {
                        xOffset = 0;
                        triangleWidth = 7;
                        triangleHeight = 7;
                        yOffset = (e.Bounds.Height - triangleHeight) / 2;

                        e.Graphics.FillPolygon(Brushes.Black, new[]
                        {
                            new Point(X + xOffset + triangleWidth, Y + yOffset),
                            new Point(X + xOffset + triangleWidth, Y + triangleHeight + yOffset),
                            new Point(X + xOffset + triangleWidth - triangleWidth, Y +triangleHeight + yOffset),
                        });
                    }
                    else
                    {
                        xOffset = 2;
                        triangleWidth = 4;
                        triangleHeight = 8;
                        yOffset = (e.Bounds.Height - triangleHeight) / 2;

                        e.Graphics.DrawPolygon(Pens.Black, new[]
                        {
                            new Point(X + xOffset, Y + yOffset),
                            new Point(X + xOffset + triangleWidth, Y + triangleHeight / 2 + yOffset),
                            new Point(X + xOffset,Y + triangleHeight + yOffset),
                        });
                    }
                }

                int textStartX = X + triangleMargin + xMargin;

                if (e.Item.Selected)
                {
                    var rect = e.Bounds;
                    rect.Inflate(-1, -1);
                    rect.Offset(textStartX - 5, 0);
                    e.Graphics.FillRectangle(Brushes.LightBlue, rect);
                }

                e.Graphics.DrawString(e.Item.Text, listView1.Font, textBrush, textStartX, Y);
                SizeF size = e.Graphics.MeasureString(e.Item.Text, listView1.Font);

                int requiredWidth = textStartX + (int)size.Width;
                if (e.Bounds.Width < requiredWidth)
                {
                    listView1.Columns[0].Width = requiredWidth + 5;
                }
            }
            else
            {
                if (e.Item.Selected)
                {
                    var rect = e.Bounds;
                    rect.Inflate(-1, -1);
                    e.Graphics.FillRectangle(Brushes.LightBlue, rect);
                }

                SizeF size = e.Graphics.MeasureString(e.SubItem.Text, listView1.Font);
                int requiredWidth = Math.Max(30, (int)size.Width + 20);
                if (listView1.Columns[e.ColumnIndex].Width < requiredWidth)
                {
                    listView1.Columns[e.ColumnIndex].Width = requiredWidth + 5;
                }

                if (!dbgObject.IsUnresolved)
                    e.Graphics.DrawString(e.SubItem.Text, listView1.Font, textBrush, e.Bounds);
            }
        }

        private void listView1_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            e.DrawDefault = true;
        }

        private void listView1_MouseDown(object sender, MouseEventArgs e)
        {
            focucedItem = listView1.GetItemAt(e.X, e.Y);

            ListViewHitTestInfo info = listView1.HitTest(e.X, e.Y);
            if (info.Item != null)
            {
                Range clickableRange = GetItemExpenderClickableRange(info.Item);
                if (clickableRange.Contains(e.X))
                {
                    OnExpandItem(info.Item);
                }
            }
        }

        void OnExpandItem(ListViewItem item)
        {
            var dbgObject = (item.Tag as DbgObject);
            if (dbgObject.HasChildren)
            {
                if (dbgObject.IsExpanded)
                {
                    foreach (var c in listView1.LootupListViewItems(dbgObject.Children))
                        listView1.Items.Remove(c);
                }
                else
                {
                    if (dbgObject.Children == null)
                    {
                        string data = Debugger.Invoke("locals", dbgObject.DbgId);
                        dbgObject.Children = ToWatchObjects(data);
                        dbgObject.HasChildren = dbgObject.Children.Any(); //readjust as complex type (e.g. array) may not have children after the deep inspection 
                        if (dbgObject.IsArray)
                        {
                            dbgObject.Value = string.Format("[{0} items]", dbgObject.Children.Count());
                            item.SubItems[1].Text = dbgObject.Value;
                        }
                    }

                    int index = listView1.IndexOfObject(dbgObject);
                    if (index != -1)
                    {
                        InsertWatchObjects(index + 1, dbgObject.Children);
                    }
                }
                dbgObject.IsExpanded = !dbgObject.IsExpanded;
                listView1.Invalidate();
            }
        }

        private ListViewItem GetItemFromPoint(ListView listView, Point mousePosition)
        {
            // translate the mouse position from screen coordinates to
            // client coordinates within the given ListView
            Point localPoint = listView.PointToClient(mousePosition);
            return listView.GetItemAt(localPoint.X, localPoint.Y);
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
            {
                try
                {
                    var buffer = new StringBuilder();

                    foreach (ListViewItem item in listView1.SelectedItems)
                    {
                        var dbgObject = item.Tag as DbgObject;
                        buffer.AppendLine(string.Format("{0} {1} {2}", dbgObject.Name, dbgObject.Value, dbgObject.Type));
                    }
                    Clipboard.SetText(buffer.ToString());

                }
                catch { }
            }
        }

        public bool IsReadOnly = true;

        private void listView1_DragEnter(object sender, DragEventArgs e)
        {
            if (!IsReadOnly && e.Data.GetDataPresent(DataFormats.StringFormat))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void listView1_DragDrop(object sender, DragEventArgs e)
        {
            if (OnDagDropText != null)
                OnDagDropText((string)e.Data.GetData(DataFormats.StringFormat));
        }

        public event Action<string> OnDagDropText;

        private void listView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Delete)
                DeleteSelected();
        }

        public void DeleteSelected()
        {
            var rootsToRemove = listView1.SelectedItems.Cast<ListViewItem>().Where(x => x.GetDbgObject().Parent == null).ToList();

            DbgObject[] rootObjectsToRemove = rootsToRemove.Select(x => x.GetDbgObject()).ToArray();

            var leafsToRemove = listView1.Items.Cast<ListViewItem>().Where(x => x.GetDbgObject().IsDescendantOfAny(rootObjectsToRemove)).ToList();

            leafsToRemove.ForEach(x => listView1.Items.Remove(x));
            rootsToRemove.ForEach(x =>
                {
                    listView1.Items.Remove(x);

                    string expressionToRemove = x.GetDbgObject().Name;

                    //there can be duplicated expressions left, which is a valid situation and the expressions need to be preserved
                    var identicalExpressions = listView1.Items
                                                        .Cast<ListViewItem>()
                                                        .Where(y => y.GetDbgObject().Name == expressionToRemove);
                    if (!identicalExpressions.Any())
                        Debugger.RemoveWatch(expressionToRemove);
                });
        }
    }

    public class Range
    {
        public int Start { get; set; }
        public int End { get; set; }
        public int Width { get { return End - Start; } }
        public bool Contains(int point)
        {
            return Start < point && point < End;
        }
    }


    static class Extensions
    {
        public static T[] AllNestedItems<T>(this T item, Func<T, IEnumerable<T>> getChildren)
        {
            int iterator = 0;
            var elementsList = new List<T>();
            var allElements = new List<T>();

            elementsList.Add(item);

            while (iterator < elementsList.Count)
            {
                foreach (T e in getChildren(elementsList[iterator]))
                {
                    elementsList.Add(e);
                    allElements.Add(e);
                }

                iterator++;
            }

            return allElements.ToArray();
        }

        public static IEnumerable<ListViewItem> ToListViewItems(this IEnumerable<DbgObject> items)
        {
            return items.Select(x => x.ToListViewItem());
        }

        public static ListViewItem ToListViewItem(this DbgObject item)
        {
            string name = item.Name;

            var li = new ListViewItem(name);
            li.SubItems.Add(item.Value);
            li.SubItems.Add(item.Type);
            li.Tag = item;
            return li;
        }

        public static DbgObject GetDbgObject(this ListViewItem item)
        {
            return item.Tag as DbgObject;
        }

        public static IEnumerable<ListViewItem> LootupListViewItems(this ListView listView, IEnumerable<DbgObject> items)
        {
            return listView.Items.Cast<ListViewItem>().Where(x => items.Contains(x.Tag as DbgObject));
        }

        public static int IndexOfObject(this ListView listView, DbgObject item)
        {
            for (int i = 0; i < listView.Items.Count; i++)
            {
                if (listView.Items[i].Tag == item)
                    return i;
            }
            return -1;
        }

        public static TabControl AddTab(this TabControl control, string tabName, Form content)
        {
            var page = new TabPage
            {
                Padding = new System.Windows.Forms.Padding(3),
                TabIndex = control.TabPages.Count,
                Text = tabName,
                UseVisualStyleBackColor = true
            };

            control.Controls.Add(page);

            content.TopLevel = false;
            content.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            content.Parent = page;
            page.Controls.Add(content);
            content.Dock = DockStyle.Fill;
            content.Visible = true;

            return control;
        }
    }
}