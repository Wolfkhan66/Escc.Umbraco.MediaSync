﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using umbraco.cms.presentation.create.controls;
using Umbraco.Core;
using Umbraco.Core.Events;
using Umbraco.Core.Models;
using Umbraco.Core.Models.EntityBase;
using Umbraco.Core.Services;

namespace Escc.Umbraco.MediaSync
{
    /// <summary>
    /// Create a relationship between content pages and the media items used by those pages
    /// </summary>
    public class MediaFileSync : ApplicationEventHandler
    {
        private readonly IMediaSyncConfigurationProvider _config = new XmlConfigurationProvider();

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaFileSync"/> class.
        /// </summary>
        public MediaFileSync()
        {
             ContentService.Saved += ContentService_Saved;
             ContentService.Copied += ContentService_Copied;
        }


        /// <summary>
        /// Updates relations after a content node has been saved.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        private void ContentService_Saved(IContentService sender, SaveEventArgs<IContent> e)
        {
            if (_config.ReadBooleanSetting("moveMediaFilesStillInUse"))
            {
                EnsureRelationTypeExists();

                foreach (var node in e.SavedEntities)
                {
                    UpdateRelationsBetweenContentAndMediaItems(node);
                }
            }
       }


        /// <summary>
        /// Copy relations to media items when a page is copied.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        void ContentService_Copied(IContentService sender, CopyEventArgs<IContent> e)
        {
            if (_config.ReadBooleanSetting("moveMediaFilesStillInUse"))
            {
                var fileRelations = uMediaSyncHelper.relationService.GetByParent(e.Original).Where(r => r.RelationType.Alias == "uMediaSyncFileRelation");
                foreach (var relation in fileRelations)
                {
                    var media = uMediaSyncHelper.mediaService.GetById(relation.ChildId);
                    var newRelation = uMediaSyncHelper.relationService.Relate(e.Copy, media, "uMediaSyncFileRelation");
                    uMediaSyncHelper.relationService.Save(newRelation);
                }
            }
        }

        private static void UpdateRelationsBetweenContentAndMediaItems(IContent node)
        {
            // Tried using ICanBeDirty and IRememberBeingDirty using the pattern from 
            // http://stackoverflow.com/questions/24035586/umbraco-memberservice-saved-event-trigger-during-login-and-get-operations
            // but although it identified the node as dirty, it didn't identify any media picker properties as dirty when they changed.

            // Get the relations as they stood before the latest save
            var uMediaSyncRelations = uMediaSyncHelper.relationService.GetByRelationTypeAlias("uMediaSyncFileRelation");
            var relationsForPageBeforeSave = uMediaSyncRelations.Where(r => r.ParentId == node.Id).ToList();
            var relatedMediaIds = relationsForPageBeforeSave.Select(r => r.ChildId).ToList();

            // Look through the properties to see what relations we have now
            var relatedMediaIdsInCurrentVersion = new List<int>();

            foreach (var propertyType in node.PropertyTypes)
            {
                if (propertyType.PropertyEditorAlias == "Umbraco.MediaPicker")
                {
                    var mediaProperty = node.Properties[propertyType.Alias];

                    if (mediaProperty.Value != null && !String.IsNullOrEmpty(mediaProperty.Value.ToString()))
                    {
                        // We've got a media picker linking to a node. Has there already been a link to the same file on this page?
                        var mediaNodeId = Int32.Parse(mediaProperty.Value.ToString());
                        if (!relatedMediaIdsInCurrentVersion.Contains(mediaNodeId))
                        {
                            relatedMediaIdsInCurrentVersion.Add(mediaNodeId);
                        }

                        // Was there a link to this item before the current save?
                        if (!relatedMediaIds.Contains(mediaNodeId))
                        {
                            // If not, create a new relation
                            var media = uMediaSyncHelper.mediaService.GetById(mediaNodeId);
                            IRelation relation = uMediaSyncHelper.relationService.Relate(node, media, "uMediaSyncFileRelation");
                            uMediaSyncHelper.relationService.Save(relation);

                            relatedMediaIds.Add(mediaNodeId);
                        }
                    }
                }
            }

            // Remove relations for any media items which were in use before the save but are now gone
            relationsForPageBeforeSave.RemoveAll(r => relatedMediaIdsInCurrentVersion.Contains(r.ChildId));
            foreach (var mediaRelation in relationsForPageBeforeSave)
            {
                uMediaSyncHelper.relationService.Delete(mediaRelation);
            }
        }

        /// <summary>
        /// Ensures the relation type exists between content nodes and the media items they use.
        /// </summary>
        private void EnsureRelationTypeExists()
        {
            if (uMediaSyncHelper.relationService.GetRelationTypeByAlias("uMediaSyncFileRelation") == null)
            {
                var relationType = new RelationType(new Guid("b796f64c-1f99-4ffb-b886-4bf4bc011a9c"), new Guid("c66ba18e-eaf3-4cff-8a22-41b16d66a972"), "uMediaSyncFileRelation", "uMediaSyncFileRelation");
                uMediaSyncHelper.relationService.Save(relationType);
            }
        }
    }
}
