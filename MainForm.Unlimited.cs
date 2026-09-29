using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RpaDesigner
{
    public sealed partial class MainForm
    {
        private readonly Dictionary<NumericUpDown,CheckBox> unlimitedInputs = new Dictionary<NumericUpDown,CheckBox>();

        private NumericUpDown UnlimitedNumber(string name,int value,int min,int max)
        {
            NumericUpDown number=Number(value,min,max);number.Name=name;
            var option=new CheckBox {Name=name+"Unlimited",Text="无限制",AutoSize=true,Dock=DockStyle.Fill,
                Margin=new Padding(6,0,0,0),TextAlign=ContentAlignment.MiddleLeft};
            option.CheckedChanged+=delegate {number.Enabled=!option.Checked;InputChanged();};
            unlimitedInputs.Add(number,option);return number;
        }
        private Control WithUnlimitedOption(Control control,string label)
        {
            var number=control as NumericUpDown;CheckBox option;
            if(number==null||!unlimitedInputs.TryGetValue(number,out option))return control;
            number.AccessibleName=label;option.AccessibleName=label+"：无限制";
            var container=new TableLayoutPanel {Name=number.Name+"LimitControl",Dock=DockStyle.Fill,
                ColumnCount=2,RowCount=1,Margin=new Padding(0,2,8,2)};
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            container.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            number.Dock=DockStyle.None;number.Anchor=AnchorStyles.Left|AnchorStyles.Right;
            number.Margin=new Padding(0,0,0,0);container.Controls.Add(number,0,0);container.Controls.Add(option,1,0);
            return container;
        }
        private bool Unlimited(NumericUpDown number)
        {
            CheckBox option;return unlimitedInputs.TryGetValue(number,out option)&&option.Checked;
        }
        private void ClearUnlimited(params NumericUpDown[] numbers)
        {
            foreach(var number in numbers){CheckBox option;if(unlimitedInputs.TryGetValue(number,out option))option.Checked=false;}
        }
    }
}
