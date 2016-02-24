﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Umbraco.Core;
using Umbraco.Core.Events;
using Umbraco.Core.Models;
using Umbraco.Core.Models.EntityBase;
using Umbraco.Core.Services;
using Constants = Escc.Umbraco.MediaSync.Helpers.Constants;
using File = System.IO.File;

namespace Escc.Umbraco.MediaSync
{
    /// <summary>
    /// Create and maintain a media folder for every content node in Umbraco
    /// </summary>
    public class uMediaSync : ApplicationEventHandler
    {
        private readonly IMediaSyncConfigurationProvider _config = new XmlConfigurationProvider();
        private readonly MediaFolderService _folderService = new MediaFolderService();

        public uMediaSync()
        {
            ContentService.Saving += ContentService_Saving;
            ContentService.Saved += ContentService_Saved;
            ContentService.Moving += ContentService_Moving;
            ContentService.Moved += ContentService_Moved;
            ContentService.Copied += ContentService_Copied;
            ContentService.Trashed += ContentService_Trashed;
            ContentService.Deleting += ContentService_Deleting;
            ContentService.EmptyingRecycleBin += ContentService_EmptyingRecycleBin;
        }

        /// <summary>
        // If the content node's name has changed, rename the media folder to match
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        void ContentService_Saving(IContentService sender, SaveEventArgs<IContent> e)
        {
            if (_config.ReadBooleanSetting("renameMedia"))
            {                
                foreach (var node in e.SavedEntities)
                {
                    if (node.HasIdentity)
                    {
                        if (uMediaSyncHelper.contentService.GetPublishedVersion(node.Id) != null)
                        {
                            IContent oldContent = uMediaSyncHelper.contentService.GetPublishedVersion(node.Id);

                            if (oldContent.Name != node.Name)
                            {
                                IEnumerable<IRelation> uMediaSyncRelations = uMediaSyncHelper.relationService.GetByParentId(node.Id).Where(r => r.RelationType.Alias == Constants.FolderRelationTypeAlias);
                                IRelation uMediaSyncRelation = uMediaSyncRelations.FirstOrDefault();

                                if (uMediaSyncRelation != null)
                                {
                                    int mediaId = uMediaSyncRelation.ChildId;

                                    IMedia media = uMediaSyncHelper.mediaService.GetById(mediaId);
                                    media.Name = node.Name;
                                    uMediaSyncHelper.mediaService.Save(media, uMediaSyncHelper.userId);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        // Ensure there's a media folder matching the content node
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        void ContentService_Saved(IContentService sender, SaveEventArgs<IContent> e)
        {
            foreach (var node in e.SavedEntities)
            {
                var dirty = (IRememberBeingDirty)node;
                var isNew = dirty.WasPropertyDirty("Id");

                if (isNew)
                {
                    _folderService.CreateRelatedMediaNode(node);
                }
                else
                {
                    if (_config.ReadBooleanSetting("checkForMissingRelations"))
                    {
                        _folderService.EnsureRelatedMediaNodeExists(node);
                    }
                }
            }
        }

        /// <summary>
        /// Write a short-lived cookie to preserve information until <see cref="ContentService_Moved"/> fires
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        void ContentService_Moving(IContentService sender, MoveEventArgs<IContent> e)
        {
            if (_config.SyncNode(e.Entity) == false)
            {
                HttpCookie uMediaSyncCookie = new HttpCookie("uMediaSyncNotMove_" + e.ParentId);
                uMediaSyncCookie.Value = e.ParentId.ToString();
                uMediaSyncCookie.Expires = DateTime.Now.AddSeconds(30);
                HttpContext.Current.Response.Cookies.Add(uMediaSyncCookie);
            }
        }

        /// <summary>
        /// When a page is moved, move its media folder too
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        void ContentService_Moved(IContentService sender, MoveEventArgs<IContent> e)
        {
            if (HttpContext.Current.Request.Cookies["uMediaSyncNotMove_" + e.Entity.ParentId] == null)
            {
                _folderService.MoveRelatedMediaNode(e.Entity);
            }
            else
            {
                HttpCookie uMediaSyncCookie = HttpContext.Current.Request.Cookies["uMediaSyncNotMove_" + e.Entity.ParentId];
                uMediaSyncCookie.Expires = DateTime.Now.AddHours(-1);
                HttpContext.Current.Response.Cookies.Add(uMediaSyncCookie);
            }
        }

       
        /// <summary>
        /// When a page is copied, copy its media folder and all its files too, then set up a relation between the two copies and publish the page
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        void ContentService_Copied(IContentService sender, CopyEventArgs<IContent> e)
        {
            if (_config.SyncNode(e.Original))
            {
                if (_config.ReadBooleanSetting("checkForMissingRelations"))
                {
                    _folderService.EnsureRelatedMediaNodeExists(e.Original);
                }

                IContent content1 = uMediaSyncHelper.contentService.GetById(e.Original.Id);
                IContent content2 = uMediaSyncHelper.contentService.GetById(e.Copy.Id);

                IEnumerable<IRelation> uMediaSyncRelationsBefore = uMediaSyncHelper.relationService.GetByParentId(content1.Id).Where(r => r.RelationType.Alias == Constants.FolderRelationTypeAlias);
                IRelation uMediaSyncRelation1 = uMediaSyncRelationsBefore.FirstOrDefault();
                if (uMediaSyncRelation1 != null)
                {
                    int media1Id = uMediaSyncRelation1.ChildId;

                    IMedia media1 = uMediaSyncHelper.mediaService.GetById(media1Id);

                    IEnumerable<IRelation> uMediaSyncRelations2 = uMediaSyncHelper.relationService.GetByParentId(content2.ParentId).Where(r => r.RelationType.Alias == Constants.FolderRelationTypeAlias);
                    IRelation uMediaSyncRelation2Parent = uMediaSyncRelations2.FirstOrDefault();

                    if (uMediaSyncRelation2Parent == null && _config.ReadBooleanSetting("checkForMissingRelations"))
                    {
                        // parent node doesn't have a media folder yet, probably because uMediaSync was installed after the node was created
                        _folderService.CreateRelatedMediaNode(content2.Parent());

                        // get the new relation for the parent
                        IEnumerable<IRelation> uMediaSyncRelationsAfter = uMediaSyncHelper.relationService.GetByParentId(content2.ParentId).Where(r => r.RelationType.Alias == Constants.FolderRelationTypeAlias);
                        uMediaSyncRelation2Parent = uMediaSyncRelationsAfter.FirstOrDefault();
                    } 

                    
                    if (uMediaSyncRelation2Parent != null)
                    {
                        int media2ParentId = uMediaSyncRelation2Parent.ChildId;

                        IMedia media2Parent = uMediaSyncHelper.mediaService.GetById(media2ParentId);

                        IMedia media2 = uMediaSyncHelper.mediaService.CreateMedia(content1.Name, media2Parent, "Folder", uMediaSyncHelper.userId);

                        uMediaSyncHelper.mediaService.Save(media2, uMediaSyncHelper.userId);

                        CopyMedia(media1, media2);

                        IRelation relation = uMediaSyncHelper.relationService.Relate(content2, media2, Constants.FolderRelationTypeAlias);
                        uMediaSyncHelper.relationService.Save(relation);

                        uMediaSyncHelper.contentService.Save(content2, uMediaSyncHelper.userId);
                    }
                }
            }
        }

        /// <summary>
        /// When a page is deleted from the recycle bin, delete its media folder too
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        void ContentService_Deleting(IContentService sender, DeleteEventArgs<IContent> e)
        {
            if (_config.ReadBooleanSetting("deleteMedia"))
            {
                foreach (var node in e.DeletedEntities)
                {
                    if (_config.SyncNode(node))
                    {
                        _folderService.DeleteRelatedMediaNode(node.Id);
                    }
                }
            }
        }


        /// <summary>
        /// When a page is moved to the recycle bin, move its media folder to the media recycle bin
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The e.</param>
        void ContentService_Trashed(IContentService sender, MoveEventArgs<IContent> e)
        {
            if (_config.ReadBooleanSetting("deleteMedia"))
            {
                if (_config.SyncNode(e.Entity))
                {
                    IEnumerable<IRelation> uMediaSyncRelations = uMediaSyncHelper.relationService.GetByParentId(e.Entity.Id).Where(r => r.RelationType.Alias == Constants.FolderRelationTypeAlias);
                    IRelation uMediaSyncRelation = uMediaSyncRelations.FirstOrDefault();
                    if (uMediaSyncRelation != null)
                    {
                        int mediaId = uMediaSyncRelation.ChildId;
                        IMedia media = uMediaSyncHelper.mediaService.GetById(mediaId);

                        // Check - does this media folder have another associated content node? It shouldn't, because it should be a one-to-one relationship, 
                        // but it is possible somehow to get into a situation where it does. If there's another content node related to this media folder, just
                        // remove the relationship to the content node being trashed. 
                        var contentRelatedToMedia = uMediaSyncHelper.relationService.GetByChildId(mediaId).Where(r => r.RelationType.Alias == Constants.FolderRelationTypeAlias);
                        if (contentRelatedToMedia.Count() > 1)
                        {
                            uMediaSyncHelper.relationService.Delete(uMediaSyncRelation);
                        }
                        else
                        {
                            // If all is normal and there's just one relationship, move the media folder to the media recycle bin as the content node moves to the content recycle bin.
                            uMediaSyncHelper.mediaService.MoveToRecycleBin(media, uMediaSyncHelper.userId);
                        }
                    }
                }
            }
        }


        /// <summary>
        /// When the content recycle bin is emptied, any media nodes related to the content being deleted should also be deleted
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="Umbraco.Core.Events.RecycleBinEventArgs"/> instance containing the event data.</param>
        void ContentService_EmptyingRecycleBin(IContentService sender, RecycleBinEventArgs e)
        {
            if (!_config.ReadBooleanSetting("deleteMedia")) return;
            
            if (!e.IsContentRecycleBin) return;

            foreach (var contentNodeId in e.Ids)
            {
                _folderService.DeleteRelatedMediaNode(contentNodeId);
            }
        }

        /// <summary>
        /// Copies the contents of one media folder to another.
        /// </summary>
        /// <param name="media1Parent">The source folder.</param>
        /// <param name="media2Parent">The destination folder.</param>
        private void CopyMedia(IMedia media1Parent, IMedia media2Parent)
        {
            var copyFiles = _config.ReadBooleanSetting("copyMediaFiles");

            if (uMediaSyncHelper.mediaService.HasChildren(media1Parent.Id))
            {
                foreach (IMedia item in uMediaSyncHelper.mediaService.GetChildren(media1Parent.Id))
                {
                    IMedia mediaItem = null;
                    if (copyFiles)
                    {
                        mediaItem = uMediaSyncHelper.mediaService.CreateMedia(item.Name, media2Parent, item.ContentType.Alias, uMediaSyncHelper.userId);

                        if (item.HasProperty("umbracoFile") && !String.IsNullOrEmpty(item.GetValue("umbracoFile").ToString()))
                        {
                            string mediaFile = item.GetValue("umbracoFile").ToString();

                            string newFile = HttpContext.Current.Server.MapPath(mediaFile);

                            if (File.Exists(newFile))
                            {
                                string fName = mediaFile.Substring(mediaFile.LastIndexOf('/') + 1);

                                FileStream fs = File.OpenRead(HttpContext.Current.Server.MapPath(mediaFile));

                                mediaItem.SetValue("umbracoFile", fName, fs);
                            }
                        }
                    } 
                    else if (item.ContentType.Alias.ToUpperInvariant() == "FOLDER")
                    {
                        mediaItem = uMediaSyncHelper.mediaService.CreateMedia(item.Name, media2Parent, item.ContentType.Alias, uMediaSyncHelper.userId);                        
                    }

                    if (mediaItem != null)
                    {
                        uMediaSyncHelper.mediaService.Save(mediaItem, uMediaSyncHelper.userId);
                        if (uMediaSyncHelper.mediaService.GetChildren(item.Id).Count() != 0)
                        {
                            CopyMedia(item, mediaItem);
                        }
                    }
                }
            }
        }
    }
}