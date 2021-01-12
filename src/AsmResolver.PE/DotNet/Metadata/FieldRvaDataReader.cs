using System;
using AsmResolver.PE.DotNet.Metadata.Blob;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;

namespace AsmResolver.PE.DotNet.Metadata
{
    /// <summary>
    /// Provides a default implementation of the <see cref="IFieldRvaDataReader"/> interface.
    /// </summary>
    public class FieldRvaDataReader : IFieldRvaDataReader
    {
        /// <inheritdoc />
        public ISegment ResolveFieldData(IErrorListener listener, IMetadata metadata, in FieldRvaRow fieldRvaRow)
        {
            if (fieldRvaRow.Data is null)
                return null;
            
            if (fieldRvaRow.Data.IsBounded)
                return fieldRvaRow.Data.GetSegment();

            if (fieldRvaRow.Data.CanRead)
            {
                var table = metadata
                    .GetStream<TablesStream>()
                    .GetTable<FieldDefinitionRow>(TableIndex.Field);

                if (fieldRvaRow.Field > table.Count)
                {
                    listener.BadImage("FieldRva row has an invalid Field column value.");
                    return null;
                }

                var field = table.GetByRid(fieldRvaRow.Field);
                int valueSize = DetermineFieldSize(metadata, field);
                
                var reader = fieldRvaRow.Data.CreateReader();
                return DataSegment.FromReader(reader, valueSize);
            }

            listener.NotSupported("FieldRva row has an invalid or unsupported data column.");
            return null;
        }

        private int DetermineFieldSize(IMetadata metadata, in FieldDefinitionRow field)
        {
            var reader = metadata
                .GetStream<BlobStream>()
                .GetBlobReaderByIndex(field.Signature);
            
            reader.ReadByte(); // calling convention attributes.
            var elementType = (ElementType) reader.ReadByte();
            return elementType switch
            {
                ElementType.Boolean => sizeof(bool),
                ElementType.Char => sizeof(char),
                ElementType.I1 => sizeof(sbyte),
                ElementType.U1 => sizeof(byte),
                ElementType.I2 => sizeof(short),
                ElementType.U2 => sizeof(ushort),
                ElementType.I4 => sizeof(int),
                ElementType.U4 => sizeof(uint),
                ElementType.I8 => sizeof(long),
                ElementType.U8 => sizeof(ulong),
                ElementType.R4 => sizeof(float),
                ElementType.R8 => sizeof(double),
                ElementType.ValueType => GetCustomTypeSize(metadata, reader),
                ElementType.Class => GetCustomTypeSize(metadata, reader),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private int GetCustomTypeSize(IMetadata metadata, IBinaryStreamReader reader)
        {
            if (!reader.TryReadCompressedUInt32(out uint codedIndex))
                return 0;

            var tablesStream = metadata.GetStream<TablesStream>();
            
            var typeToken = tablesStream
                .GetIndexEncoder(CodedIndex.TypeDefOrRef)
                .DecodeIndex(codedIndex);
            
            if (typeToken.Table == TableIndex.TypeDef)
            {
                var classLayoutTable = tablesStream.GetTable<ClassLayoutRow>(TableIndex.ClassLayout);
                if (classLayoutTable.TryGetRowByKey(2, typeToken.Rid, out var row))
                    return (int) row.ClassSize;
            }

            return 0;
        }
    }
}