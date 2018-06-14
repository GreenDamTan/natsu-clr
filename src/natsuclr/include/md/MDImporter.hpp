//
// Natsu CLR Metadata
//
#pragma once
#include "../loader/AssemblyFile.hpp"
#include "mddefs.hpp"

namespace clr
{
	namespace metadata
	{
		class MetadataStream;

		class MetadataTable
		{
		public:
			MetadataTable(size_t count);

			void Initialize(uintptr_t& content, MetadataStream& context);
			size_t GetCount() const noexcept { return count_; }
		protected:
			virtual size_t GetRowSize(MetadataStream& context) const noexcept = 0;
		private:
			uintptr_t base_;
			size_t count_;
			size_t rowSize_;
		};

		class AssemblyTable final : public MetadataTable
		{
		public:
			struct Row
			{
				uint32_t			HashAlgId;
				uint16_t			MajorVersion;
				uint16_t			MinorVersion;
				uint16_t			BuildNumber;
				uint16_t			RevisionNumber;
				AssemblyFlags		Flags;
				Sidx<stm_Blob>		PublicKey;
				Sidx<stm_String>	Name;
			};

			using MetadataTable::MetadataTable;
		protected:
			virtual size_t GetRowSize(MetadataStream& context) const noexcept override;
		};

		class MethodDefTable final : public MetadataTable
		{
		public:
			struct Row
			{
				uint32_t				RVA;
				MethodImplAttributes	ImplFlags;
				MethodAttributes		Flags;
				Sidx<stm_String>		Name;
				Sidx<stm_Blob>			Signature;
				Ridx<mdt_Param>			ParamList;
			};

			using MetadataTable::MetadataTable;
		protected:
			virtual size_t GetRowSize(MetadataStream& context) const noexcept override;
		};

		class ModuleTable final : public MetadataTable
		{
		public:
			struct Row
			{
				uint16_t			Generation;
				Sidx<stm_String>	Name;
				Sidx<stm_GUID>		Mvid;
				Sidx<stm_GUID>		EncId;
				Sidx<stm_GUID>		EncBaseId;
			};

			using MetadataTable::MetadataTable;
		protected:
			virtual size_t GetRowSize(MetadataStream& context) const noexcept override;
		};

		class TypeDefTable final : public MetadataTable
		{
		public:
			struct Row
			{
				TypeAttributes					Flags;
				Sidx<stm_String>				TypeName;
				Sidx<stm_String>				TypeNamespace;
				CodedRidx<crid_TypeDefOrRef>	Extends;
				Ridx<mdt_Field>					FieldList;
				Ridx<mdt_MethodDef>				MethodList;
			};

			using MetadataTable::MetadataTable;
		protected:
			virtual size_t GetRowSize(MetadataStream& context) const noexcept override;
		};

		class MetadataStream
		{
		public:
			void Initialize(uintptr_t content);

			size_t GetRowsCount(MetadataTables table) const noexcept;
			size_t GetSidxSize(StreamType stream) const noexcept;
			size_t GetRidxSize(MetadataTables table) const noexcept;
			size_t GetCodedRidxSize(CodedRowIndex ridxType) const noexcept;
		private:
			std::unique_ptr<MetadataTable> tables_[mdt_Count];
			uint8_t heapSizes_;
		};

		class MDImporter
		{
		public:
			MDImporter(std::shared_ptr<loader::AssemblyFile> assemblyFile);
		private:
			std::shared_ptr<loader::AssemblyFile> assemblyFile_;

			MetadataStream metaStream_;
		};
	}
}
