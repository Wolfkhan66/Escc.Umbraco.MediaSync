﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Umbraco.Core.Models;

namespace Escc.Umbraco.MediaSync
{
    /// <summary>
    /// Gets the ids of media items linked within a property that contains HTML
    /// </summary>
    public class HtmlMediaIdProvider : IRelatedMediaIdProvider
    {
        private readonly List<string> _propertyTypeAlises = new List<string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="HtmlMediaIdProvider" /> class.
        /// </summary>
        /// <param name="configurationProvider">The configuration provider.</param>
        public HtmlMediaIdProvider(IMediaSyncConfigurationProvider configurationProvider)
        {
            _propertyTypeAlises.AddRange(configurationProvider.ReadPropertyEditorAliases("htmlMediaIdProvider"));
        }

        /// <summary>
        /// Determines whether this instance can read the type of property identified by its property editor alias
        /// </summary>
        /// <param name="propertyEditorAlias">The property editor alias.</param>
        /// <returns></returns>
        public bool CanReadPropertyType(string propertyEditorAlias)
        {
            return _propertyTypeAlises.Contains(propertyEditorAlias.ToUpperInvariant());
        }

        /// <summary>
        /// Reads media ids from the property.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns></returns>
        public IEnumerable<int> ReadProperty(Property property)
        {
            var mediaIds = new List<int>();

            if (property != null && property.Value != null && !String.IsNullOrEmpty(property.Value.ToString()))
            {
                var html = new HtmlDocument();
                html.LoadHtml(property.Value.ToString());
                var mediaLinks = html.DocumentNode.SelectNodes("//a[contains(@href,'/media/')]");
                if (mediaLinks != null)
                {
                    foreach (var mediaLink in mediaLinks)
                    {
                        var uri = new Uri(mediaLink.Attributes["href"].Value, UriKind.RelativeOrAbsolute);

                        var mediaItem = uMediaSyncHelper.mediaService.GetMediaByPath(uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString());
                        if (mediaItem != null)
                        {
                            mediaIds.Add(mediaItem.Id);
                        }
                    }
                }

            }

            return mediaIds;
        }
    }
}
