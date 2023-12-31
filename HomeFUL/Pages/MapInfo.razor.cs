using Microsoft.AspNetCore.Components;
using GoogleMapsComponents;
using GoogleMapsComponents.Maps;
using GoogleMapsComponents.Maps.Places;
using Microsoft.JSInterop;

namespace HomeFUL.Pages
{
    public partial class MapInfo
    {
        [Parameter]
        public string Title { get; set; } = "";
        [Parameter]
        public string SearchTerm { get; set; } = "";
        private readonly Stack<Marker> markers = new Stack<Marker>();
        private GoogleMap map1;
        private MapOptions mapOptions;
        private AutocompleteService? autocompleteService;
        private PlacesService? placesService;
        private AutocompleteSessionToken? token;
        private Geocoder? geocoder;
        private DateTime tokenStamp = DateTime.MinValue;
        private string message;
        private ElementReference searchBox;
        private AutocompletePrediction[]? suggestions;
        double latitude;
        double longitude;
        private async Task GetLocation()
        {
            try
            {
                var location = await JSRuntime.InvokeAsync<Location>("getCurrentLocation");
                latitude = location.Lat;
                longitude = location.Lng;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        public class Location
        {
            public double Lat { get; set; }

            public double Lng { get; set; }
        }

        protected override void OnInitialized()
        {
            this.mapOptions = new MapOptions
            {
                Zoom = 13,
                Center = new LatLngLiteral
                {
                    Lat = 25.761681,
                    Lng = -80.191788
                },
                MapTypeId = MapTypeId.Roadmap
            };
        }

        private async Task OnAfterMapInit()
        {
            this.autocompleteService = await AutocompleteService.CreateAsync(this.map1.JsRuntime);
            this.placesService = await PlacesService.CreateAsync(this.map1.JsRuntime, this.map1.InteropObject);
            this.geocoder = await Geocoder.CreateAsync(this.map1.JsRuntime);
            await GetLocation();
            await map1.InteropObject.PanTo(new LatLngLiteral() { Lat = latitude, Lng = longitude });
            await SearchAsync();
        }

        private async Task<AutocompleteSessionToken> GetOrCreateTokenAsync()
        {
            if (token is null || tokenStamp == DateTime.MinValue || tokenStamp.AddMinutes(3).CompareTo(DateTime.Now) < 1)
            {
                this.token?.Dispose();
                this.token = await AutocompleteSessionToken.CreateAsync(this.map1.JsRuntime);
                this.tokenStamp = DateTime.Now;
            }

            return token;
        }

        private bool IsValidInput()
        {
            if (string.IsNullOrEmpty(SearchTerm))
            {
                this.message = "Invalid input. Please enter some text and try again.";
                return false;
            }

            return true;
        }

        private async Task ClearMarkersAsync()
        {
            while (this.markers.Count > 0)
            {
                var marker = this.markers.Pop();
                await marker.SetMap(null);
                marker.Dispose();
            }
        }

        private async Task SearchAsync()
        {
            if (autocompleteService is null || !IsValidInput())
                return;
            double offsetLat = 0.0723 * 5; // approximately 5 miles of latitude
            double offsetLng = 0.0909 * 5; // approximately 5 miles of longitude
            LatLngBoundsLiteral bounds = new LatLngBoundsLiteral
            {
                South = latitude - offsetLat, // Southwest latitude
                West = longitude - offsetLng, // Southwest longitude
                North = latitude + offsetLat, // Northeast latitude
                East = longitude + offsetLng // Northeast longitude
            };
            var request = new AutocompletionRequest
            {
                Input = SearchTerm,
                SessionToken = await GetOrCreateTokenAsync()
            };
            var response = await autocompleteService.GetPlacePredictions(request);
            if (response.Status == PlaceServiceStatus.Ok)
                this.suggestions = response.Predictions;
            else
                this.message = $"Your request failed with status code: {response.Status}";
        }

        private async Task GeocodeAsync()
        {
            if (geocoder is null || !IsValidInput())
                return;
            var response = await geocoder.Geocode(new GeocoderRequest { Address = SearchTerm });
            if (response.Status == GeocoderStatus.Ok)
            {
                await ClearMarkersAsync();
                var bounds = await LatLngBounds.CreateAsync(this.map1.JsRuntime);
                foreach (var result in response.Results)
                {
                    await RenderLocationAsync(result.FormattedAddress, result.Geometry.Location);
                    await bounds.Extend(result.Geometry.Location);
                }

                await this.map1.InteropObject.FitBounds(await bounds.ToJson(), 5);
            }
            else
                this.message = $"Your request failed with status code: {response.Status}";
        }

        private async Task GetPlaceDetailAsync(string placeId)
        {
            try
            {
                var place = await placesService.GetDetails(new PlaceDetailsRequest { PlaceId = placeId, Fields = new string[] { "address_components", "formatted_address", "geometry", "name", "place_id" }, SessionToken = await GetOrCreateTokenAsync() });
                if (place.Status == PlaceServiceStatus.Ok)
                {
                    await RenderPlaceAsync(place.Results.FirstOrDefault());
                }
                else
                    this.message = $"Your request failed with status code: {place.Status}";
            }
            finally
            {
                this?.token?.Dispose();
                this.token = null;
            }
        }

        public async Task<string> GenerateGoogleMapsLink(string toPlaceId)
        {
            string baseUrl = "https://www.google.com/maps/dir/?api=1";
            string origin = $"&origin={latitude},{longitude}";
            string travelMode = "&travelmode=transit";
            var place = await placesService.GetDetails(new PlaceDetailsRequest { PlaceId = toPlaceId, Fields = new string[] { "address_components", "formatted_address", "geometry", "name", "place_id" }, SessionToken = await GetOrCreateTokenAsync() });
            if (place.Status == PlaceServiceStatus.Ok)
            {
                string destination = $"&destination={place.Results.First().FormattedAddress}";
                await JSRuntime.InvokeVoidAsync("window.open", baseUrl + origin + destination + travelMode, "_blank");
            }

            return "";
        }

        private async Task RenderLocationAsync(string title, LatLngLiteral location)
        {
            var marker = await Marker.CreateAsync(this.map1.JsRuntime, new MarkerOptions { Position = location, Map = this.map1.InteropObject, Title = title });
            this.markers.Push(marker);
        }

        private async Task RenderPlaceAsync(PlaceResult? place)
        {
            if (place?.Geometry == null)
            {
                this.message = "No results available for " + place?.Name;
            }
            else if (place.Geometry.Location != null)
            {
                await this.map1.InteropObject.SetCenter(place.Geometry.Location);
                await this.map1.InteropObject.SetZoom(13);
                var marker = await Marker.CreateAsync(this.map1.JsRuntime, new MarkerOptions { Position = place.Geometry.Location, Map = this.map1.InteropObject, Title = place.FormattedAddress });
                this.markers.Push(marker);
                this.message = "Displaying result for " + place.Name;
            }
            else if (place.Geometry.Viewport != null)
            {
                await this.map1.InteropObject.FitBounds(place.Geometry.Viewport, 5);
                this.message = "Displaying result for " + place.Name;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ClearMarkersAsync();
            this.token?.Dispose();
            this.autocompleteService?.Dispose();
            this.placesService?.Dispose();
            this.geocoder?.Dispose();
        }
    }
}