//
// System.Drawing.Icon.cs
//
// Authors:
//   Dennis Hayes (dennish@Raytek.com)
//   Andreas Nahr (ClassDevelopment@A-SoftTech.com)
//   Sanjay Gupta (gsanjay@novell.com)
//
// Copyright (C) 2002 Ximian, Inc. http://www.ximian.com
// Copyright (C) 2004 Novell, Inc. http://www.novell.com
//

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace System.Drawing
{
	[Serializable]
	[ComVisible (false)]
	[Editor ("System.Drawing.Design.IconEditor, " + Consts.AssemblySystem_Drawing_Design, typeof (System.Drawing.Design.UITypeEditor))]
	[TypeConverter(typeof(IconConverter))]
	public sealed class Icon : MarshalByRefObject, ISerializable, ICloneable, IDisposable
	{
		[StructLayout(LayoutKind.Sequential)]
		internal struct IconDirEntry {
			internal byte	width;		// Width of icon
			internal byte	height;		// Height of icon
			internal byte	colorCount;	// colors in icon 
			internal byte	reserved;	// Reserved
			internal ushort planes;         // Color Planes
			internal ushort	bitCount;       // Bits per pixel
			internal uint	bytesInRes;     // bytes in resource
			internal uint	imageOffset;	// position in file 
		}; 

		[StructLayout(LayoutKind.Sequential)]
		internal struct IconDir {
			internal ushort			idReserved;   // Reserved
			internal ushort			idType;       // resource type (1 for icons)
			internal ushort			idCount;      // how many images?
			internal IconDirEntry []	idEntries;    // the entries for each image
		};
		
		[StructLayout(LayoutKind.Sequential)]
		internal struct BitmapInfoHeader {
            		internal uint	biSize; 
			internal int	biWidth; 
			internal int	biHeight; 
			internal ushort	biPlanes; 
			internal ushort	biBitCount; 
			internal uint	biCompression; 
			internal uint	biSizeImage; 
			internal int	biXPelsPerMeter; 
			internal int	biYPelsPerMeter; 
			internal uint	biClrUsed; 
			internal uint	biClrImportant; 
		};

		[StructLayout(LayoutKind.Sequential)]
		internal struct IconImage {
			internal BitmapInfoHeader	iconHeader;	//image header
			internal uint []		iconColors;	//colors table
			internal byte []		iconXOR;	// bits for XOR mask
			internal byte []		iconAND;	//bits for AND mask
		};	

		private Size iconSize;
		private IntPtr winHandle = IntPtr.Zero;
		private IconDir	iconDir;
		private ushort id;
		private IconImage [] imageData;
			
		private Icon ()
		{
		}
		
		public Icon (Icon original, int width, int height) : this (original, new Size(width, height))
		{			
		}

		public Icon (Icon original, Size size)
		{
			this.iconSize = size;
			this.winHandle = original.winHandle;
			this.iconDir = original.iconDir;
			this.imageData = original.imageData;
			
			int count = iconDir.idCount;
			bool sizeObtained = false;
			for (int i=0; i<count; i++){
				IconDirEntry ide = iconDir.idEntries [i];
				if (!sizeObtained)   
					if (ide.height>=size.Height && ide.width>=size.Width) {
						this.id = (ushort) i;
						sizeObtained = true;
						this.iconSize.Height = ide.height;
						this.iconSize.Width = ide.width;
						break;
					}
			}

			if (!sizeObtained){
				uint largestSize = 0;
				for (int j=0; j<count; j++){
					if (iconDir.idEntries [j].bytesInRes >= largestSize){
						largestSize = iconDir.idEntries [j].bytesInRes;
						this.id = (ushort) j;
						this.iconSize.Height = iconDir.idEntries [j].height;
						this.iconSize.Width = iconDir.idEntries [j].width;
					}
				}
			}
		}

		public Icon (Stream stream) : this (stream, 32, 32) 
		{
		}

		public Icon (Stream stream, int width, int height)
		{
			//read the icon header
			if (stream == null || stream.Length == 0)
				throw new System.ArgumentException ("The argument 'stream' must be a picture that can be used as a Icon", "stream");
			
			BinaryReader reader = new BinaryReader (stream);
            
			//iconDir = new IconDir ();
			iconDir.idReserved = reader.ReadUInt16();
			if (iconDir.idReserved != 0) //must be 0
				throw new System.ArgumentException ("Invalid Argument", "stream");
			
			iconDir.idType = reader.ReadUInt16();
			if (iconDir.idType != 1) //must be 1
				throw new System.ArgumentException ("Invalid Argument", "stream");

			ushort dirEntryCount = reader.ReadUInt16();
			iconDir.idCount = dirEntryCount;
			iconDir.idEntries = new IconDirEntry [dirEntryCount];
			imageData = new IconImage [dirEntryCount];
			bool sizeObtained = false;
			//now read in the IconDirEntry structures
			for (int i=0; i<dirEntryCount; i++){
				IconDirEntry ide;
				ide.width = reader.ReadByte ();
				ide.height = reader.ReadByte ();
				ide.colorCount = reader.ReadByte ();
				ide.reserved = reader.ReadByte ();
				ide.planes = reader.ReadUInt16 ();
				ide.bitCount = reader.ReadUInt16 ();
				ide.bytesInRes = reader.ReadUInt32 ();
				ide.imageOffset = reader.ReadUInt32 ();
				iconDir.idEntries [i] = ide;
				//is this is the best fit??
				if (!sizeObtained)   
					if (ide.height>=height && ide.width>=width) {
						this.id = (ushort) i;
						sizeObtained = true;
						this.iconSize.Height = ide.height;
						this.iconSize.Width = ide.width;
					}			
			}
			//if we havent found the best match, return the one with the
			//largest size. Is this approach correct??
			if (!sizeObtained){
				uint largestSize = 0;
				for (int j=0; j<dirEntryCount; j++){
					if (iconDir.idEntries [j].bytesInRes >= largestSize)	{
						largestSize = iconDir.idEntries [j].bytesInRes;
						this.id = (ushort) j;
						this.iconSize.Height = iconDir.idEntries [j].height;
						this.iconSize.Width = iconDir.idEntries [j].width;
					}
				}
			}
			
			//now read in the icon data
			for (int j = 0; j<dirEntryCount; j++)
			{
				IconImage iidata = new IconImage();
				BitmapInfoHeader bih = new BitmapInfoHeader();
				stream.Seek (iconDir.idEntries [j].imageOffset, SeekOrigin.Begin);
				byte [] buffer = new byte [iconDir.idEntries [j].bytesInRes];
				stream.Read (buffer, 0, buffer.Length);
				BinaryReader bihReader = new BinaryReader (new MemoryStream(buffer));
				bih.biSize = bihReader.ReadUInt32 ();
				bih.biWidth = bihReader.ReadInt32 ();
				bih.biHeight = bihReader.ReadInt32 ();
				bih.biPlanes = bihReader.ReadUInt16 ();
				bih.biBitCount = bihReader.ReadUInt16 ();
				bih.biCompression = bihReader.ReadUInt32 ();
				bih.biSizeImage = bihReader.ReadUInt32 ();
				bih.biXPelsPerMeter = bihReader.ReadInt32 ();
				bih.biYPelsPerMeter = bihReader.ReadInt32 ();
				bih.biClrUsed = bihReader.ReadUInt32 ();
				bih.biClrImportant = bihReader.ReadUInt32 ();

				iidata.iconHeader = bih;
				//Read the number of colors used and corresponding memory occupied by
				//color table. Fill this memory chunk into rgbquad[]
				int numColors;
				switch (bih.biBitCount){
					case 1: numColors = 2;
						break;
					case 4: numColors = 16;
						break;
					case 8: numColors = 256;
						break;
					default: numColors = 0;
						break;
				}
				
				iidata.iconColors = new uint [numColors];
				for (int i=0; i<numColors; i++)
					iidata.iconColors [i] = bihReader.ReadUInt32 ();

				//XOR mask is immediately after ColorTable and its size is 
				//icon height* no. of bytes per line
				
				//icon height is half of BITMAPINFOHEADER.biHeight, since it contains
				//both XOR as well as AND mask bytes
				int iconHeight = bih.biHeight/2;
				
				//bytes per line should should be uint aligned
				int numBytesPerLine = ((((bih.biWidth * bih.biPlanes * bih.biBitCount)+ 31)>>5)<<2);
				
				//Determine the XOR array Size
				int xorSize = numBytesPerLine * iconHeight;
				iidata.iconXOR = new byte [xorSize];
				for (int i=0; i<xorSize; i++)
					iidata.iconXOR[i] = bihReader.ReadByte();		
				
				//Determine the AND array size
				//For this i subtract the current position from the length.
				//ugly hack...
				int andSize = (int) (bihReader.BaseStream.Length - bihReader.BaseStream.Position);
				iidata.iconAND = new byte [andSize];
				for (int i=0; i<andSize; i++)
					iidata.iconAND[i] = bihReader.ReadByte();		
				
				imageData [j] = iidata;
				bihReader.Close();
			}			

			reader.Close();
		}

		public Icon (string fileName) : this (new FileStream (fileName, FileMode.Open))
		{			
		}

		[MonoTODO ("Implement")]
		public Icon (Type type, string resource)
		{
			using (Stream s = type.Assembly.GetManifestResourceStream (resource)) {
				if (s == null)
					throw new FileNotFoundException ("Resource name was not found: `" + resource + "'");				
			}
			throw new NotImplementedException ();
		}

		[MonoTODO ("Implement")]
       		private Icon (SerializationInfo info, StreamingContext context)
		{
			//FIXME, need to check how MS stores Icon structure
			//Will serialized form help
			throw new NotImplementedException ();
		}

		[MonoTODO ("Implement")]
		void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
		{
			throw new NotImplementedException ();
		}

		public void Dispose ()
		{
			//FIXME What needs to be called to free memory pointed by handle
			if (winHandle!=IntPtr.Zero)
				winHandle = IntPtr.Zero;
		}

		public object Clone ()
		{
			return new Icon (this, this.Width, this.Height);
		}

		[MonoTODO ("Implement")]
		public static Icon FromHandle (IntPtr handle)
		{
			throw new NotImplementedException ();
		}

		public void Save (Stream outputStream)
		{
			if (iconDir.idEntries!=null){
				BinaryWriter bw = new BinaryWriter (outputStream);
				//write icondir
				bw.Write (iconDir.idReserved);
				bw.Write (iconDir.idType);
				ushort count = iconDir.idCount;
				bw.Write (count);
				
				//now write iconDirEntries
				for (int i=0; i<(int)count; i++){
					IconDirEntry ide = iconDir.idEntries [i];
					bw.Write (ide.width);
					bw.Write (ide.height);
					bw.Write (ide.colorCount);
					bw.Write (ide.reserved);
					bw.Write (ide.planes);
					bw.Write (ide.bitCount);
					bw.Write (ide.bytesInRes);
					bw.Write (ide.imageOffset);				
				}
				
				//now write iconImage data
				for (int i=0; i<(int)count; i++){
					BitmapInfoHeader bih = imageData [i].iconHeader;
					bw.Write (bih.biSize);
					bw.Write (bih.biWidth);
					bw.Write (bih.biHeight);
					bw.Write (bih.biPlanes);
					bw.Write (bih.biBitCount);
					bw.Write (bih.biCompression);
					bw.Write (bih.biSizeImage);
					bw.Write (bih.biXPelsPerMeter);
					bw.Write (bih.biYPelsPerMeter);
					bw.Write (bih.biClrUsed);
					bw.Write (bih.biClrImportant);

					//now write color table
					int colCount = imageData [i].iconColors.Length;
					for (int j=0; j<colCount; j++)
						bw.Write (imageData [i].iconColors [j]);

					//now write XOR Mask
					bw.Write (imageData [i].iconXOR);
					
					//now write AND Mask
					bw.Write (imageData [i].iconAND);
				}
				bw.Flush();				
			}
		}

		public Bitmap ToBitmap ()
		{
			Bitmap bmp;
			if (imageData!=null){
				FileStream stream=null;
				//select active icon from the iconDirEntry
				IconImage ii = imageData [this.id];
				//MemoryStream stream = new MemoryStream ();
				//UGLY HACK.....Hate to create a FileStream and then do writing and reading back
				//but no other option as using MemoryStream causes the program to fail
				//The Constructor for Bitmap which takes a memory stream some how
				//fails everytime and i m not able to get the reason for that.
				try	{
					stream = new FileStream ("yajnas_temp.bmp", FileMode.CreateNew);
					BinaryWriter writer = new BinaryWriter (stream);
				
					//write bitmap file header
					//start with writing signature
					writer.Write ('B');
					writer.Write ('M');
				
					//now write file size
					//file size is bitmapfileheader + bitmapinfo + colorpalette + image bits
					//size of bitmapfileheader is 14 bytes, bitmapinfo is 40 bytes
					uint offSet = (uint)(14+40+ ii.iconColors.Length* 4);
					uint fileSize = (uint) (offSet + ii.iconXOR.Length);
					writer.Write (fileSize);
					
					//write reserved words
					ushort reserved12 = 0;
					writer.Write (reserved12);
					writer.Write (reserved12);
					
					//write offset
					writer.Write (offSet);
					
					//now write bitmapfile header
					BitmapInfoHeader bih = ii.iconHeader;
					writer.Write (bih.biSize);
					writer.Write (bih.biWidth);
					writer.Write (bih.biHeight/2);
					writer.Write (bih.biPlanes);
					writer.Write (bih.biBitCount);
					writer.Write (bih.biCompression);
					writer.Write (bih.biSizeImage);
					writer.Write (bih.biXPelsPerMeter);
					writer.Write (bih.biYPelsPerMeter);
					writer.Write (bih.biClrUsed);
					writer.Write (bih.biClrImportant);
					
					//now write color table
					int colCount = ii.iconColors.Length;
					for (int j=0; j<colCount; j++)
						writer.Write (ii.iconColors [j]);

					//now write image bits
					writer.Write (ii.iconXOR);

					writer.Flush();
					writer.Close();
					stream.Close();
					//create bitmap from stream and return
					bmp = new Bitmap("yajnas_temp.bmp");
					File.Delete("yajnas_temp.bmp");
				}catch (Exception e){
					throw e;
				}finally {
					stream.Close();
					File.Delete("yajnas_temp.bmp");
				}
			} else {
				bmp = new Bitmap (32, 32);
			}

			return bmp;
		}

		public override string ToString ()
		{
			//is this correct, this is what returned by .Net
			return "<Icon>";			
		}

		[Browsable (false)]
		public IntPtr Handle {
			get { 
				return winHandle;
			}
		}

		[Browsable (false)]
		public int Height {
			get {
				return iconSize.Height;
			}
		}

		public Size Size {
			get {
				return iconSize;
			}
		}

		[Browsable (false)]
		public int Width {
			get {
				return iconSize.Width;
			}
		}

		~Icon ()
		{
			Dispose ();
		}
			
	}
}
