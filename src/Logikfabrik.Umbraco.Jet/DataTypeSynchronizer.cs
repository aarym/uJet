﻿// <copyright file="DataTypeSynchronizer.cs" company="Logikfabrik">
//   Copyright (c) 2016 anton(at)logikfabrik.se. Licensed under the MIT license.
// </copyright>

namespace Logikfabrik.Umbraco.Jet
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;
    using global::Umbraco.Core.Models;
    using global::Umbraco.Core.Models.EntityBase;
    using global::Umbraco.Core.Services;

    /// <summary>
    /// The <see cref="DataTypeSynchronizer" /> class. Synchronizes model types annotated using the <see cref="DataTypeAttribute" />.
    /// </summary>
    public class DataTypeSynchronizer : ISynchronizer
    {
        private readonly IDataTypeService _dataTypeService;
        private readonly ITypeResolver _typeResolver;
        private readonly ITypeRepository _typeRepository;
        private readonly DataTypeFinder _dataTypeFinder;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataTypeSynchronizer" /> class.
        /// </summary>
        /// <param name="dataTypeService">The data type service.</param>
        /// <param name="typeResolver">The type resolver.</param>
        /// <param name="typeRepository">The type repository.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="dataTypeService" />, <paramref name="typeResolver" />, or <paramref name="typeRepository" /> are <c>null</c>.</exception>
        public DataTypeSynchronizer(
            IDataTypeService dataTypeService,
            ITypeResolver typeResolver,
            ITypeRepository typeRepository)
        {
            if (dataTypeService == null)
            {
                throw new ArgumentNullException(nameof(dataTypeService));
            }

            if (typeResolver == null)
            {
                throw new ArgumentNullException(nameof(typeResolver));
            }

            if (typeRepository == null)
            {
                throw new ArgumentNullException(nameof(typeRepository));
            }

            _dataTypeService = dataTypeService;
            _typeResolver = typeResolver;
            _typeRepository = typeRepository;
            _dataTypeFinder = new DataTypeFinder(typeRepository);
        }

        /// <summary>
        /// Synchronizes this instance.
        /// </summary>
        public void Run()
        {
            var models = _typeResolver.DataTypes.ToArray();

            if (!models.Any())
            {
                return;
            }

            new DataTypeValidator().Validate(models);

            var dataTypes = _dataTypeService.GetAllDataTypeDefinitions().ToArray();

            foreach (var model in models)
            {
                Synchronize(dataTypes, model);
            }
        }

        /// <summary>
        /// Creates a data type.
        /// </summary>
        /// <param name="model">The model to use when creating the data type.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="model" /> is <c>null</c>.</exception>
        /// <returns>The created data type.</returns>
        internal virtual IDataTypeDefinition CreateDataType(DataType model)
        {
            var dataType = new DataTypeDefinition(model.Editor)
            {
                Name = model.Name,
                DatabaseType = GetDatabaseType(model.Type)
            };

            return dataType;
        }

        /// <summary>
        /// Updates the specified data type.
        /// </summary>
        /// <param name="dataType">The data type to update.</param>
        /// <param name="model">The model to use when updating the data type.</param>
        /// <returns>The updated data type.</returns>
        internal virtual IDataTypeDefinition UpdateDataTypeDefinition(IDataTypeDefinition dataType, DataType model)
        {
            dataType.Name = model.Name;
            dataType.PropertyEditorAlias = model.Editor;
            dataType.DatabaseType = GetDatabaseType(model.Type);

            return dataType;
        }

        /// <summary>
        /// Gets the database type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The database type.</returns>
        private static DataTypeDatabaseType GetDatabaseType(Type type)
        {
            if (type == typeof(int))
            {
                return DataTypeDatabaseType.Integer;
            }

            return type == typeof(DateTime) ? DataTypeDatabaseType.Date : DataTypeDatabaseType.Ntext;
        }

        private void Synchronize(IDataTypeDefinition[] dataTypes, DataType model)
        {
            var dataType = _dataTypeFinder.Find(model, dataTypes).SingleOrDefault();

            var createNew = dataType == null;

            dataType = createNew
                ? CreateDataType(model)
                : UpdateDataTypeDefinition(dataType, model);

            _dataTypeService.Save(dataType);

            // We get the data type once more to refresh it after saving it.
            dataType = _dataTypeService.GetDataTypeDefinitionByName(dataType.Name);

            if (createNew)
            {
                // Set the pre-values, if any.
                SetDataTypePreValues(dataType, model);
            }
            else
            {
                // Update prevalues - add new ones or remove deleted.
                UpdateDataTypePreValues(dataType, model);
            }

            // Set/update tracking.
            SetDataTypeId(model, dataType);
        }

        /// <summary>
        /// Sets the data type identifier of the specified model.
        /// </summary>
        /// <param name="model">The data type model.</param>
        /// <param name="dataType">The data type.</param>
        private void SetDataTypeId(DataType model, IEntity dataType)
        {
            if (!model.Id.HasValue)
            {
                return;
            }

            _typeRepository.SetDefinitionId(model.Id.Value, dataType.Id);
        }

        private void SetDataTypePreValues(IEntity dataType, DataType model)
        {
            // If new data type and no prevalues, just save the data type
            if (model.PreValues == null || !model.PreValues.Any())
            {
                _dataTypeService.SavePreValues(dataType.Id, new Dictionary<string, PreValue>());
                return;
            }

            var preValues = model.PreValues.ToDictionary(preValue => preValue.Key, v => new PreValue(v.Value));

            _dataTypeService.SavePreValues(dataType.Id, preValues);
        }

        private void UpdateDataTypePreValues(IEntity dataType, DataType model)
        {
            // If prevalues is null, do nothing
            if (model.PreValues == null)
            {
                return;
            }

            // If prevalues is an empty dictionary, existing will be cleared
            var newPreValues = new Dictionary<string, PreValue>();

            var existingPreValuesCollection = _dataTypeService.GetPreValuesCollectionByDataTypeId(dataType.Id);

            if (existingPreValuesCollection.IsDictionaryBased)
            {
                var existingPreValues = existingPreValuesCollection.PreValuesAsDictionary;
                foreach (var p in model.PreValues)
                {
                    if (!existingPreValues.ContainsKey(p.Key))
                    {
                        newPreValues.Add(p.Key, new PreValue(p.Value));
                    }
                    else
                    {
                        var newPreValue = existingPreValues[p.Key];

                        if (newPreValue.Value != p.Value)
                        {
                            newPreValue.Value = p.Value;
                        }

                        newPreValues.Add(p.Key, newPreValue);
                    }
                }

                _dataTypeService.SavePreValues(dataType.Id, newPreValues);
            }
        }
    }
}