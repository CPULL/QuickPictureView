using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace QPV {

  public class IManager {
    private int currentImg = 0;
    private bool[] loaded;
    private int[] positions;
    List<ImageInfo> images;
    List<int> sequence;
    List<string> folders;
    Dictionary<string, JObject> ratings;
    MainWindow win;
    Random random;
    private int pos = -1;
    private DispatcherTimer starsTimer;
    private int starsToGo;
    private int totalRated;
    public bool needSaving;


    public IManager(MainWindow mainWindow) {
      needSaving = false;
      currentImg = 0;
      loaded = new bool[5];
      positions = new int[5];
      for (int i = 0; i < 5; i++) {
        loaded[i] = false;
        positions[i] = -1;
      }
      win = mainWindow;
      SetImageVisibility(0);
      images = new List<ImageInfo>();
      sequence = new List<int>();
      folders = new List<string>();
      ratings = new Dictionary<string, JObject>();
      random = new Random();
      starsTimer = new DispatcherTimer();
      starsTimer.Interval = TimeSpan.FromMilliseconds(5);
      starsTimer.Tick += AlterStars;
    }

    private void AlterStars(object sender, EventArgs e) {
      if (win.Stars.Width == starsToGo) {
        starsTimer.Stop();
        return;
      }
      win.Stars.Width += (win.Stars.Width < starsToGo) ? 4 : -4;
    }

    internal void Add(string path, string name) {
      sequence.Add(images.Count);
      images.Add(new ImageInfo(path, name, -1));

      if (!folders.Contains(path))
        folders.Add(path);
    }

    internal void Complete() {
      if (images.Count == 0) {
        MessageBox.Show("No images found", "QPV");
        return;
      }

      // Initialize all Ratings for the paths that are missing in the Dictionary
      for (int i = 0; i < folders.Count; i++) {
        if (!ratings.ContainsKey(folders[i]))
          ratings[folders[i]] = new JObject();
      }

      // Get the ratings from the Json
      for (int i = 0; i < images.Count; i++) {
        string path = images[i].Path;
        try {
          JObject json = ratings[path];
          if (json[images[i].ImageName] == null || json[images[i].ImageName]["R"] == null)
            images[i].Rating = -1;
          else {
            images[i].Rating = json[images[i].ImageName]["R"].ToObject<int>();
          }
          if (json[images[i].ImageName] == null || json[images[i].ImageName]["T"] == null)
            images[i].Tags.Clear();
          else {
            JArray ts = (JArray)json[images[i].ImageName]["T"];
            for (int t = 0; t < ts.Count; t++)
              images[i].Tags.Add(json[images[i].ImageName]["T"][t].ToObject<int>());
          }

        } catch (Exception) {
          images[i].Rating = -1;
          images[i].Tags.Clear();
        }
      }

      Randomize();

      // Remove from Json all keys that are no more valid
      foreach (string path in ratings.Keys) {
        JObject json = ratings[path];
        List<string> toRemove = new List<string>();
        foreach (JProperty property in json.Properties()) {
          bool found = false;
          for (int i = 0; i < images.Count; i++)
            if (images[i].ImageName == property.Name && images[i].Path == path) {
              found = true;
              break;
            }
          if (!found) toRemove.Add(property.Name);
        }
        for (int i = 0; i < toRemove.Count; i++) {
          json.Remove(toRemove[i]);
          needSaving = true;
        }
      }

      totalRated = 0;
      for (int i = 0; i < images.Count; i++) {
        if (images[i].Rating > 0)
          totalRated++;
      }
    }

    internal void ShowPrevImage() {
      if (sequence.Count == 0) return;
      pos--;
      if (pos == -1)
        pos = sequence.Count - 1;
      ShowImg(ShowMode.Prev);
    }
    internal void ShowNextImage() {
      pos++;
      if (pos == sequence.Count)
        pos = 0;
      ShowImg(ShowMode.Next);
    }

    internal void AlterImage(bool prev) {
      if (pos == -1 || sequence.Count == 0) return;
      if (prev) {
        sequence[pos]--;
        if (sequence[pos] == -1)
          sequence[pos] = images.Count - 1;
      }
      else {
        sequence[pos]++;
        if (sequence[pos] == images.Count)
          sequence[pos] = 0;
      }
      ShowImg(ShowMode.Same);
    }

    private Image GetImagePlaceForBufferPosition(int ipos) {
      switch (ipos) {
        case 1: return win.Image1;
        case 2: return win.Image2;
        case 3: return win.Image3;
        case 4: return win.Image4;
        case 0:
        default:
          return win.Image0;
      }
    }

    private void LoadImage(int nextPos, string path) {
      Image im = GetImagePlaceForBufferPosition(nextPos);
      try {
        BitmapImage bm = new BitmapImage();
        bm.BeginInit();
        bm.UriSource = new Uri(path);
        bm.EndInit();
        im.Source = bm;
      } catch (Exception) {
        Uri oUri = new Uri("pack://application:,,,/Images/NotValid.png", UriKind.RelativeOrAbsolute);
        im.Source = BitmapFrame.Create(oUri);
      }
      loaded[nextPos] = true;
    }

    internal void ShowImg(ShowMode sm) {
      if (sequence.Count == 0) return;
      if (pos < 0) pos = 0;

      if (sm == ShowMode.Next) {
        // Check if the next image is already loaded with the current expected position
        int next = (currentImg + 1) % 5;
        if (!loaded[next] || positions[next] != sequence[pos]) { // No, load
          LoadImage(next, images[sequence[pos]].Path + "\\" + images[sequence[pos]].ImageName);
          positions[next] = sequence[pos];
        }
        // Pre-load the next 2
        int place = (currentImg + 2) % 5;
        PreloadImageThread(place, pos + 1);
        place = (place + 1) % 5;
        PreloadImageThread(place, pos + 2);
        // Discard the image-2 (make it free)
        int toDiscard = (currentImg - 2) % 5;
        if (toDiscard < 0) toDiscard += 5;
        loaded[toDiscard] = false;
        // Increase the curimage
        currentImg = next;
        SetImageVisibility(next);
      }
      else if (sm == ShowMode.Same) {
        // Just load, ignore pre-buffering
        if (!loaded[currentImg] || positions[currentImg] != sequence[pos]) {
          LoadImage(currentImg, images[sequence[pos]].Path + "\\" + images[sequence[pos]].ImageName);
          positions[currentImg] = sequence[pos];
        }
        SetImageVisibility(currentImg);
      }
      else if (sm == ShowMode.Prev) {
        // Check if the prev image is already loaded with the current expected position
        int prev = (currentImg - 1) % 5;
        if (prev < 0) prev += 5;
        if (!loaded[prev] || positions[prev] != sequence[pos]) { // No, load
          LoadImage(prev, images[sequence[pos]].Path + "\\" + images[sequence[pos]].ImageName);
          positions[prev] = sequence[pos];
        }
        // Pre-load the prev 2
        int place = (currentImg - 2) % 5;
        if (place < 0) place += 5;
        PreloadImageThread(place, pos - 1);
        place--;
        if (place < 0) place += 5;
        PreloadImageThread(place, pos - 2);
        // Discard the image+2 (make it free)
        loaded[(currentImg + 2) % 5] = false;
        // Decrease the curimage
        currentImg = prev;
        SetImageVisibility(prev);
      }

      if (win.ratingsVisible) {
        win.Stars.Visibility = Visibility.Visible;
        // Do we have a rating?
        int rating = images[sequence[pos]].Rating;
        if (rating < 0) rating = 0;
        if (rating > 5) rating = 5;
        starsToGo = 32 * rating;
        starsTimer.Start();
      }
      else
        win.Stars.Visibility = Visibility.Hidden;

      // Do we have tags?
      ShowImageTags(images[sequence[pos]]);

      string title = "QPV ";
      switch (win.mode) {
        case Mode.Normal: title += "Normal "; break;
        case Mode.Slide: title += "SlideShow (" + win.slideShowTime / 10 + " seconds) "; break;
        case Mode.FilterSort: title += "Filtering "; break;
        case Mode.RatingsTagging: title += "Tagging "; break;
      }
      title += images[sequence[pos]].ImageName + " [" + pos + "/" + sequence.Count + " " + totalRated + "/" + images.Count + "]";
      win.Title = title;
    }



    public void PreloadImageThread(int place, int thePos) {
      if (thePos < 0 || thePos >= sequence.Count) return;
      var t = new Thread(() => RealImageLoad(place, thePos));
      t.Start();
      return;
    }

    private void RealImageLoad(int place, int thePos) {
      if (loaded[place] && positions[place] == sequence[thePos]) return;
      GetImagePlaceForBufferPosition(place).Dispatcher.Invoke(new Action(() => {
        LoadImage(place, images[sequence[thePos]].Path + "\\" + images[sequence[thePos]].ImageName);
        positions[place] = sequence[thePos];
      }), DispatcherPriority.ContextIdle);
    }


    private void SetImageVisibility(int iPos) {
      win.Image0.Visibility = iPos == 0 ? Visibility.Visible : Visibility.Hidden;
      win.Image1.Visibility = iPos == 1 ? Visibility.Visible : Visibility.Hidden;
      win.Image2.Visibility = iPos == 2 ? Visibility.Visible : Visibility.Hidden;
      win.Image3.Visibility = iPos == 3 ? Visibility.Visible : Visibility.Hidden;
      win.Image4.Visibility = iPos == 4 ? Visibility.Visible : Visibility.Hidden;
    }

    internal void SetStars(int rating) {
      if (pos == -1) return;
      if (images[sequence[pos]].Rating != rating) {
        totalRated++;
        needSaving = true;
      }
      images[sequence[pos]].Rating = rating;
      starsToGo = 32 * rating;
      starsTimer.Start();
    }

    internal void SaveJson() {
      if (!needSaving) return;
      for (int i = 0; i < images.Count; i++) {
        JObject json = ratings[images[i].Path];
        json[images[i].ImageName] = new JObject();
        json[images[i].ImageName]["R"] = images[i].Rating;
        JArray a = new JArray();
        foreach (int t in images[i].Tags) {
          a.Add(t);
        }
        json[images[i].ImageName]["T"] = a;
      }

      foreach (string path in ratings.Keys) {
        string js = JsonConvert.SerializeObject(ratings[path]);
        File.Delete(path + "\\ratings.json");
        File.WriteAllText(path + "\\ratings.json", js, System.Text.Encoding.UTF8);
      }
    }

    internal void SetFilter() {
      // Check if we need to check tags
      int i;
      bool checkTags = win.OnlyNoTags.IsChecked ?? false;
      if (!checkTags) {
        for (i = 0; i < win.enabledTags.Count; i++)
          if (win.enabledTags[i].TagMode!=0) {
            checkTags = true;
            break;
          }
      }


      // Reconstruct the sequence by putting only the values with the given rating and filters
      sequence = new List<int>();
      switch (win.chosenFilter) {
        case FilterMode.All:
          for (i = 0; i < images.Count; i++)
            if (!checkTags || TagsRespected(images[i]))
              sequence.Add(i);
          break;

        case FilterMode.Unrated:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating == -1 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating0:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating >= 0 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating0E:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating == 0 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating1:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating >= 1 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating1E:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating == 1 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating2:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating >= 2 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating2E:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating == 2 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating3:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating >= 3 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating3E:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating == 3 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating4:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating >= 4 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating4E:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating == 4 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating5:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating >= 5 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;

        case FilterMode.Rating5E:
          for (i = 0; i < images.Count; i++)
            if (images[i].Rating == 5 && (!checkTags || TagsRespected(images[i])))
              sequence.Add(i);
          break;
      }
      pos = 0;

      // Apply any sort defined
      Sort();

      if (sequence.Count == 0) {
        MessageBox.Show("No images found", "QPV");
        return;
      }

      ShowImg(ShowMode.Same);
    }

    private bool TagsRespected(ImageInfo img) {
      if (win.OnlyNoTags.IsChecked ?? false)
        return img.Tags.Count == 0;

      if (win.tagsInAndMode) {
        for (int i = 0; i < win.enabledTags.Count; i++) {
          if (win.enabledTags[i].TagMode == 1 && !img.Tags.Contains(win.enabledTags[i].ID)) return false;
          if (win.enabledTags[i].TagMode == -1 && img.Tags.Contains(win.enabledTags[i].ID)) return false;
        }
        return true;
      }

      for (int i = 0; i < win.enabledTags.Count; i++) {
        if (win.enabledTags[i].TagMode == 1 && img.Tags.Contains(win.enabledTags[i].ID)) return true;
        if (win.enabledTags[i].TagMode == -1 && !img.Tags.Contains(win.enabledTags[i].ID)) return true;
      }

      return false;
    }

    internal void Randomize() {
      for (int i = 0; i < sequence.Count * 10; i++) {
        int r1 = random.Next(0, sequence.Count - 1);
        int r2 = random.Next(0, sequence.Count - 1);
        int tmp = sequence[r1];
        sequence[r1] = sequence[r2];
        sequence[r2] = tmp;
      }
    }

    internal void AddRatings(string path, string ratingFullName) {
      try {
        JObject r = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(ratingFullName));
        ratings[path] = r;
      } catch (Exception) {
        // No ratings
      }
    }

    public int CompareImagesRatingAscending(int left, int right) {
      return images[left].Rating - images[right].Rating;
    }
    public int CompareImagesRatingDescending(int left, int right) {
      return images[right].Rating - images[left].Rating;
    }
    public int CompareImagesNameAscending(int left, int right) {
      return String.CompareOrdinal(images[left].ImageName, images[right].ImageName);
    }
    public int CompareImagesNameDescending(int left, int right) {
      return String.CompareOrdinal(images[right].ImageName, images[left].ImageName);
    }
    public int CompareImagesPathAscending(int left, int right) {
      return String.CompareOrdinal(images[left].Path, images[right].Path);
    }
    public int CompareImagesPathDescending(int left, int right) {
      return String.CompareOrdinal(images[right].Path, images[left].Path);
    }

    internal void Sort() {
      switch (win.chosenSort) {
        case SortMode.None: break;
        case SortMode.Random: Randomize(); break;
        case SortMode.RatingA: sequence.Sort(CompareImagesRatingAscending); break;
        case SortMode.RatingD: sequence.Sort(CompareImagesRatingDescending); break;
        case SortMode.NameA: sequence.Sort(CompareImagesNameAscending); break;
        case SortMode.NameD: sequence.Sort(CompareImagesNameDescending); break;
        case SortMode.PathA: sequence.Sort(CompareImagesPathAscending); break;
        case SortMode.PathD: sequence.Sort(CompareImagesPathDescending); break;
      }
    }

    internal int GetImagesCount() {
      return images.Count;
    }

    internal void SetPos(bool begin) {
      if (sequence.Count == 0) return;
      if (begin) {
        pos = 0;
        ShowImg(ShowMode.Prev);
      }
      else {
        pos = sequence.Count - 1;
        ShowImg(ShowMode.Next);
      }
    }

    internal void FilterFolder() {
      string path = images[sequence[pos]].Path;
      string oldImageForNewPos = images[sequence[pos]].ImageName;
      List<int> toRemove = new List<int>();

      for (int i = 0; i < sequence.Count; i++)
        if (images[sequence[i]].Path != path)
          toRemove.Add(sequence[i]);

      for (int i = 0; i < toRemove.Count; i++)
        sequence.Remove(toRemove[i]);

      // Find the old position
      pos = 0; // Just in case we will not find it
      for (int i = 0; i < sequence.Count; i++)
        if (images[sequence[i]].ImageName == oldImageForNewPos) {
          pos = i;
          break;
        }

      ShowImg(ShowMode.Same);
    }

    internal void FindFirstRating(int rating) {
      for (int place = pos; place < sequence.Count; place++) {
        if (images[sequence[place]].Rating == rating) {
          pos = place;
          return;
        }
      }

      // Do the previous
      for (int place = 0; place < pos; place++) {
        if (images[sequence[place]].Rating == rating) {
          pos = place;
          return;
        }
      }
    }

    internal bool HasFiles() {
      return images.Count > 0;
    }

    internal ImageInfo GetImageInfo() {
      if (pos < 0 || images.Count == 0 || sequence[pos] >= images.Count) return null;
      return images[sequence[pos]];
    }

    internal void CleanUp() {
      if (needSaving)
        SaveJson();

      needSaving = false;
      currentImg = 0;
      for (int i = 0; i < 5; i++) {
        loaded[i] = false;
        positions[i] = -1;
      }
      SetImageVisibility(0);
      images.Clear();
      folders.Clear();
      ratings.Clear();
      sequence.Clear();
      starsTimer.Stop();
      win.Stars.Width = 0;
      pos = 0;
    }

    internal bool NoImageShown() {
      return pos < 0 || sequence[pos] >= images.Count;
    }

    internal void ShowImageTags(ImageInfo img) {
      for (int i = 0; i < win.TagButtons.Children.Count; i++) {
        win.TagButtons.Children[i].Visibility = Visibility.Collapsed;
        win.TagButtons.Children[i].Opacity = 1.0;
        ((TextBox)((StackPanel)win.TagButtons.Children[i]).Children[0]).BorderBrush = win.buttonTagBrushOut;
        ((TextBox)((StackPanel)win.TagButtons.Children[i]).Children[0]).Background = win.buttonTagBrushIn;
      }
      if (win.ratingsVisible && img != null) {
        foreach (int t in img.Tags) {
          if (win.tagRefs.ContainsKey(t)) {
            win.tagRefs[t].button.Visibility = Visibility.Visible;
            win.tagRefs[t].button.Opacity = 1.0;
            ((TextBox)win.tagRefs[t].button.Children[0]).BorderBrush = win.buttonTagBrushAct;
          }
        }
      }
    }

    internal List<DuplicateImage> FindDuplicates() {
      List<DuplicateImage> dups = new List<DuplicateImage>();
      // Get the size and first Kb of all known images
      for (int i = 0; i < images.Count; i++) {
        if (images[i].Size == 0) {
          images[i].Size = new FileInfo(images[i].Path + "\\" + images[i].ImageName).Length;
          // Load the first Kb
          images[i].FirstKb = new byte[1024];
          FileStream fs = File.OpenRead(images[i].Path + "\\" + images[i].ImageName);
          fs.Read(images[i].FirstKb, 0, 1024);
        }
      }

      // Now compare size and first kb
      for (int i = 0; i < images.Count; i++) {
        for (int j = 0; j < images.Count; j++) {
          if (i == j) continue;
          bool same = false;
          if (images[i].Size == images[j].Size) {
            same = true;
            // Check the KB
            for (int k = 0; k < 1024; k++)
              if (images[i].FirstKb[k] != images[j].FirstKb[k]) {
                same = false;
                break;
              }
            if (same) {
              dups.Add(new DuplicateImage(images[i].Path + "\\" + images[i].ImageName, images[j].Path + "\\" + images[j].ImageName, i));
            }
          }
        }
      }
      return dups;
    }

    internal void PreviewImg(int imgIndex) {
      try {
        BitmapImage bm = new BitmapImage();
        bm.BeginInit();
        bm.UriSource = new Uri(images[imgIndex].Path + "\\" + images[imgIndex].ImageName);
        bm.EndInit();
        win.PreviewImage.Source = bm;
      } catch (Exception) {
        Uri oUri = new Uri("pack://application:,,,/Images/NotValid.png", UriKind.RelativeOrAbsolute);
        win.PreviewImage.Source = BitmapFrame.Create(oUri);
      }
    }

    internal void RemoveImage(int imgIndex) {
      images.RemoveAt(imgIndex);
      int seqPos = -1;
      for (int i = 0; i < sequence.Count; i++)
        if (sequence[i]==imgIndex) {
          seqPos = i;
          break;
        }
      if (seqPos != -1)
        sequence.RemoveAt(seqPos);
    }

    internal void RecalculateDuplicateIndexes(List<DuplicateImage> dups) {
      for(int i=0; i<dups.Count; i++) {
        for (int j=0; j<images.Count; j++) {
          string path = images[j].Path + "\\" + images[j].ImageName;
          if (dups[i].Path == path || dups[i].Duplicate == path) {
            dups[i].ImgIndex = j;
            break;
          }
        }
      }
    }
  }

  public enum Mode {
    Normal = 0, // No automatic time, Left, Right will change the image randomly [F1]
    Slide = 1, // Automatic progress of slide show, Left/Right will still work  [F2]
    RatingsTagging = 2, // Define Tags and Rating of the image [F3]
    FilterSort = 3, // Filters the images, Sort and Randomize the images [F4]
    TagDefs = 5 // Define the list of Tags [F5]
  }
  public enum ShowMode {
    Prev = 0,
    Same = 1,
    Next = 2
  }

  public enum FilterMode {
    All, Unrated,
    Rating0, Rating1, Rating2, Rating3, Rating4, Rating5,
    Rating0E, Rating1E, Rating2E, Rating3E, Rating4E, Rating5E
  };

  public enum SortMode {
    None, Random, RatingA, RatingD, NameA, NameD, PathA, PathD
  }

  public partial class MainWindow : Window {
    public IManager im;
    public Mode mode;
    public Mode prevMode;
    public DispatcherTimer timer;
    public int slideShowTime = 20;
    WindowState oldStatus;
    WindowStyle oldStyle;
    bool showingHelp = false;
    public Dictionary<int, TagRef> tagRefs;
    ObservableCollection<TagDef> definitionOfTags;
    public ObservableCollection<TagDef> enabledTags;
    public ObservableCollection<QuickTag> quickTags;
    public int quickTag = -1;
    public List<TagsPerImage> imageTags;
    public bool tagsAltered = false;
    public bool ratingsVisible = true;
    public FilterMode chosenFilter = FilterMode.All;
    public SortMode chosenSort = SortMode.None;

    public MainWindow() {
      InitializeComponent();

      oldStatus = WindowState;
      oldStyle = WindowStyle;
      Height = (System.Windows.SystemParameters.PrimaryScreenHeight * 0.8);
      Width = (System.Windows.SystemParameters.PrimaryScreenWidth * 0.8);
      KeyDown += new KeyEventHandler(OnButtonKeyDown);

      im = new IManager(this);

      mode = Mode.Normal;
      prevMode = Mode.Normal;
      timer = new DispatcherTimer();
      timer.Interval = TimeSpan.FromSeconds(slideShowTime / 10);
      timer.Tick += Timer_Tick;

      Title = "QPV  (No images loaded, press O or L to load)";

      tagRefs = new Dictionary<int, TagRef>();
      try {
        JObject jt = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText("Tags.json"));
        foreach (JProperty property in jt.Properties()) {
          int id = int.Parse(property.Name);
          string tag = jt[property.Name].ToString();
          StackPanel sp = CreateTagButton(id, tag); // Create a button with this name
          tagRefs.Add(id, new TagRef(tag, id, sp));
        }
        nextTagID = 0;
        if (tagRefs.Count > 0) {
          foreach (int i in tagRefs.Keys)
            if (i > nextTagID) nextTagID = i;
          nextTagID++;
        }
      } catch (Exception e) {
        Trace.WriteLine(e.Message);
      }
      definitionOfTags = new ObservableCollection<TagDef>();
      enabledTags = new ObservableCollection<TagDef>();
      quickTags = new ObservableCollection<QuickTag>();
      foreach (int i in tagRefs.Keys) {
        TagDef td = new TagDef(i, tagRefs[i].tag, false);
        definitionOfTags.Add(td);
        enabledTags.Add(td);
        quickTags.Add(new QuickTag(tagRefs[i].tag, i));
      }
      definitionOfTags.Add(new TagDef(nextTagID, "", true));
      imageTags = new List<TagsPerImage>();
      dgTags.ItemsSource = definitionOfTags;
      FilterTags.ItemsSource = enabledTags;
      QuickTags.ItemsSource = quickTags;

      ShowFilters(false);
    }

    private int nextTagID;
    private string currentTag = "";
    private int numPossibleTagsFound = 0;

    private void OnButtonKeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Escape) {
        im.SaveJson();
        Close();
        return;
      }
      else if (e.Key == Key.F11) {
        if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None) {
          WindowState = oldStatus;
          WindowStyle = oldStyle;
        }
        else {
          oldStatus = WindowState;
          oldStyle = WindowStyle;
          WindowState = WindowState.Maximized;
          WindowStyle = WindowStyle.None;
        }
        return;
      }

      // Help
      if (mode != Mode.RatingsTagging && mode != Mode.TagDefs && e.Key == Key.H) {
        if (showingHelp) {
          Help.Visibility = Visibility.Hidden;
          Stars.Visibility = Visibility.Visible;
          if (!im.HasFiles())
            StartMessage.Visibility = Visibility.Visible;
          showingHelp = false;
          return;
        }
        if (!showingHelp) {
          Help.Visibility = Visibility.Visible;
          Stars.Visibility = Visibility.Hidden;
          StartMessage.Visibility = Visibility.Hidden;
          showingHelp = true;
          return;
        }
      }
      if (showingHelp) return;

      if (e.Key == Key.OemTilde) {
        ratingsVisible = !ratingsVisible;
        if (ratingsVisible)
          Stars.Visibility = Visibility.Visible;
        else
          Stars.Visibility = Visibility.Hidden;
        im.ShowImageTags(im.GetImageInfo());
      }

      ShowFilters(false);

      // Switch modes
      if (e.Key == Key.F1) {
        timer.Stop();
        dgTags.Visibility = Visibility.Hidden;
        mode = Mode.Normal;
        Title = "QPV Normal";
        MakeTagCloseButtonAvailable(false);
        ShowFilters(false);
        return;
      }
      else if (e.Key == Key.F2) {
        if (!im.HasFiles() || im.NoImageShown()) return;
        timer.Start();
        dgTags.Visibility = Visibility.Hidden;
        mode = Mode.Slide;
        Title = "SlideShow (" + slideShowTime / 10 + " seconds)";
        MakeTagCloseButtonAvailable(false);
        ShowFilters(false);
        return;
      }
      else if (e.Key == Key.F3) {
        if (!im.HasFiles() || im.NoImageShown()) return;
        timer.Stop();
        dgTags.Visibility = Visibility.Hidden;
        prevMode = (mode == Mode.Slide ? Mode.Slide : Mode.Normal);
        mode = Mode.RatingsTagging;
        Title = "QPV Tagging";
        CalculateImageTags();
        MakeTagCloseButtonAvailable(true);
        ShowFilters(false);
        currentTag = "";
        numPossibleTagsFound = 0;
        return;
      }
      else if (e.Key == Key.F4) {
        if (!im.HasFiles() || im.NoImageShown()) return;
        timer.Stop();
        dgTags.Visibility = Visibility.Hidden;
        Stars.Width = 0;
        prevMode = (mode == Mode.Slide ? Mode.Slide : Mode.Normal);
        mode = Mode.FilterSort;
        Title = "QPV Filtering/Sorting";
        MakeTagCloseButtonAvailable(false);
        ShowFilters(true);
        return;
      }
      else if (e.Key == Key.F5) {
        StartMessage.Visibility = Visibility.Hidden;
        ShowFilters(false);
        timer.Stop();
        mode = Mode.TagDefs;
        if (tagRefs.Count == 0) {
          NoTags.Visibility = Visibility.Visible;
          return;
        }
        dgTags.Visibility = Visibility.Visible;
        Stars.Width = 0;
        return;
      }

      // Normal mode
      if (mode == Mode.Normal) {
        if (e.Key == Key.Left)
          im.ShowPrevImage();
        else if (e.Key == Key.Right)
          im.ShowNextImage();
        else if (e.Key == Key.Up)
          im.AlterImage(true);
        else if (e.Key == Key.Down)
          im.AlterImage(false);
        else if (e.Key == Key.Home)
          im.SetPos(true);
        else if (e.Key == Key.End)
          im.SetPos(false);
        else if (e.Key == Key.F) {
          im.FilterFolder();
          Title = "QPV Folder";
        }
        else if (e.Key == Key.O) {
          StartMessage.Visibility = Visibility.Hidden;
          bool recursive = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
          LoadImages(true, recursive);
          if (!im.HasFiles()) {
            StartMessage.Visibility = Visibility.Visible;
          }
        }
        else if (e.Key == Key.D) { // Find duplicates
          if (!im.HasFiles()) return;
          List<DuplicateImage> dups = im.FindDuplicates();
          if (dups.Count == 0) {
            MessageBox.Show("No duplicates found.", "QPV");
            return;
          }
          Stars.Width = 0;
          PreviewImage.Visibility = Visibility.Visible;

          // Show them as list, with the ability to preview the selected files (as normal images)
          LDuplicates.Visibility = Visibility.Visible;
          Duplicates.Visibility = Visibility.Visible;
          Duplicates.ItemsSource = null;
          Duplicates.ItemsSource = dups;
        }
        else if (e.Key == Key.L) {
          StartMessage.Visibility = Visibility.Hidden;
          bool recursive = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
          LoadImages(false, recursive);
          if (!im.HasFiles()) {
            StartMessage.Visibility = Visibility.Visible;
          }
        }
        return;
      }

      // SlideShow
      if (mode == Mode.Slide) {
        if (e.Key == Key.Left)
          im.ShowPrevImage();
        else if (e.Key == Key.Space) {
          if (timer.IsEnabled)
            timer.Stop();
          else
            timer.Start();
        }
        else if (e.Key == Key.Add || e.Key == Key.OemPlus) {
          if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            slideShowTime += 5;
          else
            slideShowTime++;
          if (slideShowTime > 600) slideShowTime = 600;
          timer.Interval = TimeSpan.FromMilliseconds(slideShowTime * 100);
          TimerLabel.Content = slideShowTime / 10.0 + " seconds";
          TimerLabel.Visibility = Visibility.Visible;
        }
        else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) {
          if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            slideShowTime -= 5;
          else
            slideShowTime--;
          if (slideShowTime < 1) slideShowTime = 1;
          timer.Interval = TimeSpan.FromMilliseconds(slideShowTime * 100);
          TimerLabel.Content = slideShowTime / 10.0 + " seconds";
          TimerLabel.Visibility = Visibility.Visible;
        }
        else if (e.Key == Key.D0)
          im.FindFirstRating(0);
        else if (e.Key == Key.D1)
          im.FindFirstRating(1);
        else if (e.Key == Key.D2)
          im.FindFirstRating(2);
        else if (e.Key == Key.D3)
          im.FindFirstRating(3);
        else if (e.Key == Key.D4)
          im.FindFirstRating(4);
        else if (e.Key == Key.D5)
          im.FindFirstRating(5);

        return;
      }

      // Ratings
      if (mode == Mode.RatingsTagging) {
        if (e.Key == Key.Left) {
          im.ShowPrevImage();
          currentTag = "";
          numPossibleTagsFound = 0;
          CalculateImageTags();
          return;
        }
        else if (e.Key == Key.Right) {
          im.ShowNextImage();
          currentTag = "";
          numPossibleTagsFound = 0;
          CalculateImageTags();
          return;
        }
        else if (e.Key == Key.D0) {
          im.SetStars(0);
          return;
        }
        else if (e.Key == Key.D1) {
          im.SetStars(1);
          return;

        }
        else if (e.Key == Key.D2) {
          im.SetStars(2);
          return;

        }
        else if (e.Key == Key.D3) {
          im.SetStars(3);
          return;
        }
        else if (e.Key == Key.D4) {
          im.SetStars(4);
          return;
        }
        else if (e.Key == Key.D5) {
          im.SetStars(5);
          return;
        }

        if (quickTag!=-1 && e.Key==Key.Q) {
          ImageInfo img = im.GetImageInfo();
          if (img == null) return;
          if (img.Tags.Contains(quickTag))
            img.Tags.Remove(quickTag);
          else
            img.Tags.Add(quickTag);
          im.ShowImageTags(img);
          return;
        }


        // Tagging
        if (tagRefs.Count == 0) return;

        if (numPossibleTagsFound > 0) {
          if (e.Key == Key.Up) {
            // Select the previous tag. Select the last if none found
            for (int i = imageTags.Count - 1; i >= 0; i--) {
              if (imageTags[i].selected) {
                // Find the previous valid (not associated and possible
                int prev = -1;
                for (int j = i - 1; j >= 0; j--) {
                  if (!imageTags[j].associated && imageTags[j].validPerImage) {
                    prev = j;
                    break;
                  }
                }
                if (prev == -1) {
                  // Find from the bottom
                  for (int j = imageTags.Count - 1; j >= i; j--) {
                    if (!imageTags[j].associated && imageTags[j].validPerImage) {
                      prev = j;
                      break;
                    }
                  }
                }
                // Select the new tag
                SelectImageTag(i, false);
                SelectImageTag(prev, true);
                return;
              }
            }
            // If we got here we had nothing selected, get the last valid one
            for (int i = imageTags.Count - 1; i >= 0; i--) {
              if (!imageTags[i].associated && imageTags[i].validPerImage) {
                SelectImageTag(i, true);
              }
            }
            return;
          }

          else if (e.Key == Key.Down) {
            // Select the next valid one
            for (int i = 0; i < imageTags.Count; i++) {
              if (imageTags[i].selected) {
                // Find the next valid one
                int next = -1;
                for (int j = i + 1; j < imageTags.Count; j++) {
                  if (!imageTags[j].associated && imageTags[j].validPerImage) {
                    next = j;
                    break;
                  }
                }
                if (next == -1) {
                  // Select the first valid one
                  for (int j = 0; j < i; j++) {
                    if (!imageTags[j].associated && imageTags[j].validPerImage) {
                      next = j;
                      break;
                    }
                  }
                }
                // Select the new tag
                SelectImageTag(i, false);
                SelectImageTag(next, true);
                return;
              }
            }
            // If we got here we had nothing selected, get the first valid one
            for (int i = 0; i < imageTags.Count; i++) {
              if (!imageTags[i].associated && imageTags[i].validPerImage) {
                SelectImageTag(i, true);
                return;
              }
            }
            return;
          }

          else if (e.Key == Key.Enter || e.Key == Key.Tab || e.Key == Key.Space) {
            for (int i = 0; i < imageTags.Count; i++) {
              if (imageTags[i].selected) {
                imageTags[i].associated = true;
                ImageInfo img = im.GetImageInfo();
                if (img != null && !img.Tags.Contains(imageTags[i].id))
                  img.Tags.Add(imageTags[i].id);
                imageTags[i].opacity = 1.0;
                SelectImageTag(i, false);
                im.needSaving = true;

                return;
              }
            }
          }

        }

        char c = GetCharFromKey(e.Key);
        if (c == ' ' && e.Key != Key.Space) return;
        if (c == '\b' && currentTag.Length > 0) {
          currentTag = currentTag.Substring(0, currentTag.Length - 1);
          CheckTags('\0');
          return;
        }
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c == ' ' || c == '-' || c == '+' || c == '_' || c == '*')) {
          CheckTags(c);
        }

        return;
      }

      // Filtering
      if (mode == Mode.FilterSort) {
        if (e.Key == Key.D0) {
          if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            chosenFilter = FilterMode.Rating0E;
          else
            chosenFilter = FilterMode.Rating0;
          im.SetFilter();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.D1) {
          if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            chosenFilter = FilterMode.Rating1E;
          else
            chosenFilter = FilterMode.Rating1;
          im.SetFilter();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.D2) {
          if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            chosenFilter = FilterMode.Rating2E;
          else
            chosenFilter = FilterMode.Rating2;
          im.SetFilter();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.D3) {
          if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            chosenFilter = FilterMode.Rating3E;
          else
            chosenFilter = FilterMode.Rating3;
          im.SetFilter();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.D4) {
          if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            chosenFilter = FilterMode.Rating4E;
          else
            chosenFilter = FilterMode.Rating4;
          im.SetFilter();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.D5) {
          if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            chosenFilter = FilterMode.Rating5E;
          else
            chosenFilter = FilterMode.Rating5;
          im.SetFilter();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.Space) {
          if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            chosenFilter = FilterMode.Unrated;
          else
            chosenFilter = FilterMode.All;
          for (int i = 0; i < enabledTags.Count; i++)
            enabledTags[i].TagMode = 0;
          im.SetFilter();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }

        else if (e.Key == Key.S) {
          chosenSort = (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) ? SortMode.RatingD : SortMode.RatingA;
          im.Sort();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.N) {
          chosenSort = (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) ? SortMode.NameD : SortMode.NameA;
          im.Sort();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.P) {
          chosenSort = (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) ? SortMode.PathD : SortMode.PathA;
          im.Sort();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.R) {
          chosenSort = SortMode.None;
          im.Randomize();
          mode = prevMode;
          im.ShowImg(ShowMode.Same);
          ShowFilters(false);
        }
        else if (e.Key == Key.Home)
          im.SetPos(true);
        else if (e.Key == Key.End)
          im.SetPos(false);
        return;
      }

    }

    private void ShowFilters(bool show) {
      Visibility v = show ? Visibility.Visible : Visibility.Hidden;

      // These two are always set to invisible
      LDuplicates.Visibility = Visibility.Hidden;
      Duplicates.Visibility = Visibility.Hidden;
      PreviewImage.Visibility = Visibility.Hidden;

      TagsAndOrMode.Visibility = v;
      FilterTags.Visibility = v;
      ApplyTags.Visibility = v;
      OnlyNoTags.Visibility = v;
      Randomize.Visibility = v;
      FilterByRating0.Visibility = v;
      FilterByRating1.Visibility = v;
      FilterByRating2.Visibility = v;
      FilterByRating3.Visibility = v;
      FilterByRating4.Visibility = v;
      FilterByRating5.Visibility = v;
      FilterByExactRating0.Visibility = v;
      FilterByExactRating1.Visibility = v;
      FilterByExactRating2.Visibility = v;
      FilterByExactRating3.Visibility = v;
      FilterByExactRating4.Visibility = v;
      FilterByExactRating5.Visibility = v;
      SortByNameA.Visibility = v;
      SortByNameD.Visibility = v;
      SortByRatingA.Visibility = v;
      SortByRatingD.Visibility = v;
      SortByPathA.Visibility = v;
      SortByPathD.Visibility = v;

      LRatings.Visibility = v;
      LSorting.Visibility = v;
      RRa.Visibility = v;
      RRu.Visibility = v;
      RR0.Visibility = v;
      RR0o.Visibility = v;
      RR1.Visibility = v;
      RR1o.Visibility = v;
      RR2.Visibility = v;
      RR2o.Visibility = v;
      RR3.Visibility = v;
      RR3o.Visibility = v;
      RR4.Visibility = v;
      RR4o.Visibility = v;
      RR5.Visibility = v;
      RR5o.Visibility = v;

      RSn.Visibility = v;
      RSr.Visibility = v;
      RSna.Visibility = v;
      RSrd.Visibility = v;
      RSra.Visibility = v;
      RSnd.Visibility = v;
      RSpa.Visibility = v;
      RSpd.Visibility = v;

      LQuickTags.Visibility = v;
      QuickTags.Visibility = v;

      if (show) {
        switch(chosenFilter) {
          case FilterMode.All: RRa.IsChecked = true; break;
          case FilterMode.Unrated: RRu.IsChecked = true; break;
          case FilterMode.Rating0: RR0.IsChecked = true; break;
          case FilterMode.Rating0E: RR0o.IsChecked = true; break;
          case FilterMode.Rating1: RR1.IsChecked = true; break;
          case FilterMode.Rating1E: RR1o.IsChecked = true; break;
          case FilterMode.Rating2: RR2.IsChecked = true; break;
          case FilterMode.Rating2E: RR2o.IsChecked = true; break;
          case FilterMode.Rating3: RR3.IsChecked = true; break;
          case FilterMode.Rating3E: RR3o.IsChecked = true; break;
          case FilterMode.Rating4: RR4.IsChecked = true; break;
          case FilterMode.Rating4E: RR4o.IsChecked = true; break;
          case FilterMode.Rating5: RR5.IsChecked = true; break;
          case FilterMode.Rating5E: RR5o.IsChecked = true; break;
        }
        switch (chosenSort) {
          case SortMode.None: RSn.IsChecked = true; break;
          case SortMode.Random: RSr.IsChecked = true; break;
          case SortMode.RatingA: RSra.IsChecked = true; break;
          case SortMode.RatingD: RSrd.IsChecked = true; break;
          case SortMode.NameA: RSna.IsChecked = true; break;
          case SortMode.NameD: RSnd.IsChecked = true; break;
          case SortMode.PathA: RSpa.IsChecked = true; break;
          case SortMode.PathD: RSpd.IsChecked = true; break;
        }
      }

    }

    private void MakeTagCloseButtonAvailable(bool available) {
      Visibility v = available ? Visibility.Visible : Visibility.Hidden;
      foreach (int key in tagRefs.Keys)
        tagRefs[key].button.Children[1].Visibility = v;
    }

    private void SelectImageTag(int pos, bool selected) {
      if (pos == -1) return;
      imageTags[pos].selected = selected;
      if (imageTags[pos].associated) {
        ((TextBox)tagRefs[imageTags[pos].id].button.Children[0]).BorderBrush = buttonTagBrushAct;
        ((TextBox)tagRefs[imageTags[pos].id].button.Children[0]).Background = buttonTagBrushIn;
        tagRefs[imageTags[pos].id].button.Opacity = 1.0;
      }
      else if (selected) {
        ((TextBox)tagRefs[imageTags[pos].id].button.Children[0]).BorderBrush = buttonTagBrushOut;
        ((TextBox)tagRefs[imageTags[pos].id].button.Children[0]).Background = buttonTagBrushSel;
        if (tagRefs[imageTags[pos].id].button.Opacity < 0.8)
          tagRefs[imageTags[pos].id].button.Opacity = 0.8;
      }
      else {
        ((TextBox)tagRefs[imageTags[pos].id].button.Children[0]).BorderBrush = buttonTagBrushOut;
        ((TextBox)tagRefs[imageTags[pos].id].button.Children[0]).Background = buttonTagBrushIn;
        for (int i = 0; i < imageTags.Count; i++) {
          if (imageTags[i].id == imageTags[pos].id) {
            tagRefs[imageTags[pos].id].button.Opacity = imageTags[i].opacity;
            break;
          }
        }
      }
    }

    private void CalculateImageTags() {
      numPossibleTagsFound = 0;
      currentTag = "";
      ImageInfo img = im.GetImageInfo();
      imageTags.Clear();
      if (img == null) return;

      foreach (int key in tagRefs.Keys) {
        imageTags.Add(new TagsPerImage(key, tagRefs[key].tag, false, img.Tags.Contains(key)));
      }
    }

    private void CheckTags(char c) {
      // Show the tags of the current image
      ImageInfo img = im.GetImageInfo();
      im.ShowImageTags(img);

      if (c != '\0')
        currentTag += c;
      currentTag = currentTag.ToLowerInvariant();
      numPossibleTagsFound = 0;
      foreach (int key in tagRefs.Keys) {
        string tag = tagRefs[key].tag.ToLowerInvariant();
        if (tag.IndexOf(currentTag) != -1) {
          if (tag == currentTag) { // Exaxtly the same
            tagRefs[key].button.Visibility = Visibility.Visible;
            tagRefs[key].button.Opacity = 1.0;
            SetTagAsPossible(key, true);
          }
          else { // Just partial content
                 // Show the one selected and handle cursors, and enter
            tagRefs[key].button.Visibility = Visibility.Visible;
            tagRefs[key].button.Opacity = 1.0 * currentTag.Length / tag.Length;
            SetTagAsPossible(key, true, 1.0 * currentTag.Length / tag.Length);
          }
          numPossibleTagsFound++;
        }
        else if (!img.Tags.Contains(key)) {
          tagRefs[key].button.Visibility = Visibility.Collapsed;
          SetTagAsPossible(key, false);
        }
      }
      if (numPossibleTagsFound == 0) {
        if (currentTag.Length > 1 && c != '\0') {
          currentTag = "";
          CheckTags(c);
        }
      }
    }

    private void SetTagAsPossible(int key, bool possible) {
      SetTagAsPossible(key, possible, 1.0);
    }
    private void SetTagAsPossible(int key, bool possible, double opacity) {
      for (int i = 0; i < imageTags.Count; i++)
        if (imageTags[i].id == key) {
          imageTags[i].validPerImage = possible;
          tagRefs[key].button.Visibility = possible ? Visibility.Visible : Visibility.Collapsed;
          if (imageTags[i].associated || imageTags[i].selected)
            tagRefs[key].button.Opacity = 1.0;
          else
            tagRefs[key].button.Opacity = opacity;
          imageTags[i].opacity = opacity;
          return;
        }
    }

    public enum MapType : uint {
      MAPVK_VK_TO_VSC = 0x0,
      MAPVK_VSC_TO_VK = 0x1,
      MAPVK_VK_TO_CHAR = 0x2,
      MAPVK_VSC_TO_VK_EX = 0x3,
    }

    [DllImport("user32.dll")]
    public static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 4)] StringBuilder pwszBuff, int cchBuff, uint wFlags);

    [DllImport("user32.dll")]
    public static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, MapType uMapType);

    public static char GetCharFromKey(Key key) {
      char ch = ' ';

      int virtualKey = KeyInterop.VirtualKeyFromKey(key);
      byte[] keyboardState = new byte[256];
      GetKeyboardState(keyboardState);

      uint scanCode = MapVirtualKey((uint)virtualKey, MapType.MAPVK_VK_TO_VSC);
      StringBuilder stringBuilder = new StringBuilder(2);

      int result = ToUnicode((uint)virtualKey, scanCode, keyboardState, stringBuilder, stringBuilder.Capacity, 0);
      switch (result) {
        case -1:
          break;
        case 0:
          break;
        case 1: {
          ch = stringBuilder[0];
          break;
        }
        default: {
          ch = stringBuilder[0];
          break;
        }
      }
      return ch;
    }



    public SolidColorBrush buttonTagBrushIn = new SolidColorBrush() {
      Color = Color.FromArgb(239, 171, 173, 179)
    };
    public SolidColorBrush buttonTagBrushOut = new SolidColorBrush() {
      Color = Color.FromArgb(255, 198, 198, 198)
    };
    public SolidColorBrush buttonTagBrushSel = new SolidColorBrush() {
      Color = Color.FromArgb(255, 255, 98, 98)
    };
    public SolidColorBrush buttonTagBrushAct = new SolidColorBrush() {
      Color = Color.FromArgb(255, 98, 98, 255)
    };

    private void LoadImages(bool replace, bool recursive) {
      CommonOpenFileDialog d = new CommonOpenFileDialog {
        IsFolderPicker = true
      };

      if (replace && recursive) {
        if (im.HasFiles())
          d.Title = "Select Images Folder (Recursive) existing images will be replaced!";
        else
          d.Title = "Select Images Folder (Recursive)";
      }
      else if (replace && !recursive) {
        if (im.HasFiles())
          d.Title = "Select Images Folder (Not Recursive) existing images will be replaced!";
        else
          d.Title = "Select Images Folder (Not Recursive)";
      }
      else if (!replace && recursive)
        d.Title = "Select Images Folder to add (Recursive)";
      else if (!replace && !recursive)
        d.Title = "Select Images Folder to add (Not Recursive)";

      if (d.ShowDialog() != CommonFileDialogResult.Ok)
        return;

      if (replace) {
        Title = "QPV Loading Images" + (recursive ? " (Recursive)" : "");
        im.CleanUp();
      }
      else
        Title = "QPV Adding Images" + (recursive ? " (Recursive)" : "");

      // Get all image files in the directory, get also the json ratings if any
      string[] allfiles = Directory.GetFiles(d.FileName, "*.*", (recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
      foreach (var file in allfiles) {
        FileInfo info = new FileInfo(file);
        string ext = info.Extension.ToLowerInvariant();
        if (ext.Length < 2) continue; // Not interesting
        ext = ext.Substring(1);

        if (info.Name.ToLowerInvariant() == "ratings.json") {
          im.AddRatings(info.DirectoryName, info.FullName);
          continue;
        }

        if (ext != "jpg" && ext != "jpeg" && ext != "gif" && ext != "png") continue; // Not interesting

        // Add to the list and check for the ratings
        im.Add(info.DirectoryName, info.Name);
      }

      im.Complete();
      if (im.HasFiles())
        Title = "QPV " + im.GetImagesCount() + " images loaded";
      else
        Title = "QPV  (No images loaded, press O or L to load)";
    }

    void Timer_Tick(object sender, EventArgs e) {
      im.ShowNextImage();
      TimerLabel.Visibility = Visibility.Hidden;
    }

    private void SaveJson(object sender, System.ComponentModel.CancelEventArgs e) {
      if (im != null) im.SaveJson();
      if (tagRefs.Count > 0) {
        JObject js = new JObject();
        foreach (int id in tagRefs.Keys)
          js[id.ToString()] = tagRefs[id].tag;

        File.Delete("Tags.json");
        File.WriteAllText("Tags.json", JsonConvert.SerializeObject(js), System.Text.Encoding.UTF8);
      }
    }



    private StackPanel CreateTagButton(int id, string name) {
      StackPanel sp = new StackPanel {
        Orientation = Orientation.Horizontal,
        Name = "Button_Tag_" + id,
        Visibility = Visibility.Collapsed
      };
      TextBox tb = new TextBox {
        BorderBrush = buttonTagBrushOut,
        Background = buttonTagBrushIn,
        Width = 150,
        Text = name,
        IsReadOnly = true
      };
      Button b = new Button {
        Content = new Image {
          Source = new BitmapImage(new Uri("pack://application:,,,/Images/Close.png")),
          Width = 16
        }
      };
      b.Click += new RoutedEventHandler(OnRemoveImageTag);
      sp.Children.Add(tb);
      sp.Children.Add(b);
      TagButtons.Children.Add(sp);
      return sp;
    }


    private void OnRemoveTag(object sender, RoutedEventArgs e) {
      TagDef td = (TagDef)((Button)e.Source).DataContext;
      // Ask for confirmation
      if (MessageBox.Show("You sure you want to remove the tag\n" + td.TagName + "?", "QPV", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

      // Remove the tag
      if (definitionOfTags.Contains(td))
        definitionOfTags.Remove(td);
      if (tagRefs.ContainsKey(td.ID))
        tagRefs.Remove(td.ID);
      if (enabledTags.Contains(td))
        enabledTags.Remove(td);
      TagButtons.Children.Remove(tagRefs[td.ID].button);

      UpdateGridThread();
      UpdateEnabledTagsGridThread();
      tagsAltered = true;
    }

    private bool _handleTagEdits = true;
    private void OnRanameTag(object sender, DataGridRowEditEndingEventArgs e) {
      if (_handleTagEdits) {
        if (((TagDef)e.Row.Item).TagName == "") return; // Was an insert
        _handleTagEdits = false;
        dgTags.CommitEdit();
        TagDef td = (TagDef)e.Row.Item;
        // Still unique and not null?

        if (td.TagName == "") {
          MessageBox.Show("Empty tags are not allowed!", "QPV", MessageBoxButton.OK, MessageBoxImage.Exclamation);
          td.TagName = tagRefs[td.ID].tag;
          UpdateGridThread();
          _handleTagEdits = true;
          return;
        }

        // Check if it is unique
        string tag = td.TagName;
        foreach (int t in tagRefs.Keys)
          if (tagRefs[t].tag == tag) {
            MessageBox.Show("The tag is not unique!", "QPV", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            td.TagName = tagRefs[td.ID].tag;
            UpdateGridThread();
            _handleTagEdits = true;
            return;
          }

        // Save
        tagRefs[td.ID].tag = td.TagName;
        ((TextBox)tagRefs[td.ID].button.Children[0]).Text = td.TagName;
        tagsAltered = true;
        _handleTagEdits = true;
      }
    }


    private void OnAddTag(object sender, RoutedEventArgs e) {
      TagDef td = (TagDef)((Button)e.Source).DataContext;

      if (td.TagName == "") {
        MessageBox.Show("Empty tags are not allowed!", "QPV", MessageBoxButton.OK, MessageBoxImage.Exclamation);
        return;
      }

      // Check if it is unique
      string tag = td.TagName;
      foreach (int t in tagRefs.Keys)
        if (tagRefs[t].tag.ToLowerInvariant() == tag.ToLowerInvariant()) {
          MessageBox.Show("The tag is not unique!", "QPV", MessageBoxButton.OK, MessageBoxImage.Exclamation);
          return;
        }

      // Add tag to the lists, and create its button
      StackPanel sp = CreateTagButton(td.ID, td.TagName);
      tagRefs.Add(td.ID, new TagRef(tag, td.ID, sp));
      nextTagID++;

      // Add a new last row
      td.IsLast = false;
      TagDef ntd = new TagDef(nextTagID, "", true);
      definitionOfTags.Add(ntd);
      enabledTags.Add(td);


      UpdateGridThread();
      UpdateEnabledTagsGridThread();
      tagsAltered = true;
    }


    public void UpdateGridThread() {
      var t = new Thread(() => UpdateGrid());
      t.Start();
      return;
    }

    private void UpdateGrid() {
      dgTags.Dispatcher.Invoke(new Action(() => {
        dgTags.ItemsSource = null;
        dgTags.ItemsSource = definitionOfTags;
      }), DispatcherPriority.ContextIdle);
    }

    public void UpdateEnabledTagsGridThread() {
      var t = new Thread(() => UpdateEnabledTagsGrid());
      t.Start();
      return;
    }

    private void UpdateEnabledTagsGrid() {
      FilterTags.Dispatcher.Invoke(new Action(() => {
        FilterTags.ItemsSource = null;
        FilterTags.ItemsSource = enabledTags;
      }), DispatcherPriority.ContextIdle);
    }

    private void OnRemoveImageTag(object sender, RoutedEventArgs e) {
      if (mode != Mode.RatingsTagging) return;
      string sid = ((StackPanel)((Button)sender).Parent).Name.Substring(11);
      int id = -1;
      int.TryParse(sid, out id);
      ImageInfo img = im.GetImageInfo();
      if (img == null) return;
      if (img.Tags.Contains(id))
        img.Tags.Remove(id);
      im.ShowImageTags(img);
      im.needSaving = true;
    }

    public bool tagsInAndMode = true;
    private void OnChangeAndOr(object sender, RoutedEventArgs e) {
      tagsInAndMode = !tagsInAndMode;
      TagsAndOrMode.Content = tagsInAndMode ? "AND mode" : "OR mode";
      UpdateEnabledTagsGridThread();
    }

    private void OnSortFilterClick(object sender, RoutedEventArgs e) {
      if (sender == SortByRatingA) {
        chosenSort = SortMode.RatingA;
        im.Sort();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == SortByRatingD) {
        chosenSort = SortMode.RatingD;
        im.Sort();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == SortByNameA) {
        chosenSort = SortMode.NameA;
        im.Sort();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == SortByNameD) {
        chosenSort = SortMode.NameD;
        im.Sort();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == SortByPathA) {
        chosenSort = SortMode.PathA;
        im.Sort();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == SortByPathD) {
        chosenSort = SortMode.PathD;
        im.Sort();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == Randomize) {
        chosenSort = SortMode.None;
        im.Randomize();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByRating0) {
        chosenFilter = FilterMode.Rating0;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByRating1) {
        chosenFilter = FilterMode.Rating1;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByRating2) {
        chosenFilter = FilterMode.Rating2;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByRating3) {
        chosenFilter = FilterMode.Rating3;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByRating4) {
        chosenFilter = FilterMode.Rating4;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByRating5) {
        chosenFilter = FilterMode.Rating5;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByExactRating0) {
        chosenFilter = FilterMode.Rating0E;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByExactRating1) {
        chosenFilter = FilterMode.Rating1E;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByExactRating2) {
        chosenFilter = FilterMode.Rating2E;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByExactRating3) {
        chosenFilter = FilterMode.Rating3E;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByExactRating4) {
        chosenFilter = FilterMode.Rating4E;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      else if (sender == FilterByExactRating5) {
        chosenFilter = FilterMode.Rating5E;
        im.SetFilter();
        mode = prevMode;
        im.ShowImg(ShowMode.Same);
      }
      ShowFilters(false);
    }


    private void OnChangeOnlyNoTags(object sender, RoutedEventArgs e) {
      if (((CheckBox)sender).IsChecked ?? false) {
        for (int i = 0; i < enabledTags.Count; i++)
          enabledTags[i].TagMode = 0;
        UpdateEnabledTagsGridThread();
      }
    }

    private void OnSelectingFilter(object sender, SelectionChangedEventArgs e) {
      TagDef td = (TagDef)FilterTags.SelectedItem;
      if (td == null) return;
      td.TagMode++;
      if (td.TagMode == 2)
        td.TagMode = -1;
      UpdateEnabledTagsGridThread();
    }

    private void OnSelectingQuickTag(object sender, SelectionChangedEventArgs e) {
      QuickTag qt = (QuickTag)QuickTags.SelectedItem;
      if (qt == null) return;
      if (qt.Selected) {
        quickTag = -1;
        qt.Selected = false;
      }
      else
        foreach (QuickTag q in quickTags) {
          if (q == qt) {
            quickTag = q.Index;
            q.Selected = true;
          }
          else
            q.Selected = false;
        }
      QuickTags.Dispatcher.Invoke(new Action(() => {
        QuickTags.ItemsSource = null;
        QuickTags.ItemsSource = quickTags;
      }), DispatcherPriority.ContextIdle);
    }

    private void OnApplyTagFilters(object sender, RoutedEventArgs e) {
      mode = prevMode;
      Title = "QPV";
      MakeTagCloseButtonAvailable(false);
      ShowFilters(false);
      im.SetFilter();
    }

    private void OnRadio(object sender, RoutedEventArgs e) {
      if (sender == RSn) chosenSort = SortMode.None;
      else if (sender == RSr) chosenSort = SortMode.Random;
      else if (sender == RSra) chosenSort = SortMode.RatingA;
      else if (sender == RSrd) chosenSort = SortMode.RatingD;
      else if (sender == RSna) chosenSort = SortMode.NameA;
      else if (sender == RSnd) chosenSort = SortMode.NameD;
      else if (sender == RSpa) chosenSort = SortMode.PathA;
      else if (sender == RSpd) chosenSort = SortMode.PathD;

      else if (sender == RRa) chosenFilter = FilterMode.All;
      else if (sender == RRu) chosenFilter = FilterMode.Unrated;
      else if (sender == RR0) chosenFilter = FilterMode.Rating0;
      else if (sender == RR0o) chosenFilter = FilterMode.Rating0E;
      else if (sender == RR1) chosenFilter = FilterMode.Rating1;
      else if (sender == RR1o) chosenFilter = FilterMode.Rating1E;
      else if (sender == RR2) chosenFilter = FilterMode.Rating2;
      else if (sender == RR2o) chosenFilter = FilterMode.Rating2E;
      else if (sender == RR3) chosenFilter = FilterMode.Rating3;
      else if (sender == RR3o) chosenFilter = FilterMode.Rating3E;
      else if (sender == RR4) chosenFilter = FilterMode.Rating4;
      else if (sender == RR4o) chosenFilter = FilterMode.Rating4E;
      else if (sender == RR5) chosenFilter = FilterMode.Rating5;
      else if (sender == RR5o) chosenFilter = FilterMode.Rating5E;
    }

    private void OnPreviewDuplicate(object sender, SelectionChangedEventArgs e) {
      DuplicateImage di = (DuplicateImage)Duplicates.SelectedItem;
      if (di != null)
        im.PreviewImg(di.ImgIndex);
    }

    private void OnDeleteImage(object sender, RoutedEventArgs e) {
      DuplicateImage di = (DuplicateImage)((Button)e.Source).DataContext;
      if (di == null) return;
      if (MessageBox.Show("Are you sure to delete the file:\n" + di.Duplicate + "?", "QPV", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No) return;

      // Delete the file and remove the item from the list
      PreviewImage.Source = null;
      GC.Collect();
      GC.WaitForPendingFinalizers();
      Thread.Sleep(100);
      try {
        File.Delete(di.Duplicate);
      } catch (Exception ex) {
        MessageBox.Show("Cannot delete the file\n" + di.Duplicate + "\n\n" + ex.Message, "QPV", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }
      im.RemoveImage(di.ImgIndex);
      ((List<DuplicateImage>)Duplicates.ItemsSource).Remove(di);

      // Find all other places in duplicates where the removed file was
      bool hasFile(DuplicateImage h) => (h.Path == di.Duplicate || h.Duplicate == di.Duplicate);
      ((List<DuplicateImage>)Duplicates.ItemsSource).RemoveAll(hasFile);

      // We need to re-calculate the indexes of the remaining images
      im.RecalculateDuplicateIndexes((List<DuplicateImage>)Duplicates.ItemsSource);

      // Update the Grid
      Duplicates.Dispatcher.Invoke(new Action(() => {
        List<DuplicateImage> dups = (List<DuplicateImage>)Duplicates.ItemsSource;
        Duplicates.ItemsSource = null;
        Duplicates.ItemsSource = dups;
      }), DispatcherPriority.ContextIdle);
    }
  }


  public class TagDef {
    public TagDef(int i, string t, bool l) {
      ID = i;
      TagName = t;
      IsLast = l;
      TagMode = 0;
    }

    public int ID { get; set; }
    public string TagName { get; set; }
    public bool IsLast { get; set; }
    public int TagMode { get; set; } // -1 should not have, 0 ignore, 1 must have
  }

  public class ImageInfo {
    public string ImageName;
    public string Path;
    public int Rating;
    public HashSet<int> Tags;

    public long Size;
    public byte[] FirstKb;

    public ImageInfo(string path, string name, int rating) {
      ImageName = name;
      Rating = rating;
      Path = path;
      Tags = new HashSet<int>();
      Size = 0;
    }
  }

  public class DuplicateImage {
    public string Path { get; set; }
    public string Duplicate { get; set; }
    public int ImgIndex;

    public DuplicateImage(string path, string duplicate, int imgIndex) {
      Path = path;
      Duplicate = duplicate;
      ImgIndex = imgIndex;
    }
  }

  public class TagRef {
    public string tag;
    public int id;
    public StackPanel button;

    public TagRef(string t, int i, StackPanel b) {
      tag = t;
      id = i;
      button = b;
    }
  }

  public class TagsPerImage {
    public int id;
    public string tag;
    public bool validPerImage;
    public bool selected;
    public bool associated;
    public double opacity;

    public TagsPerImage(int i, string t, bool s, bool a) {
      id = i;
      tag = t;
      validPerImage = false;
      selected = s;
      associated = a;
      opacity = 1.0;
    }
  }

  public class QuickTag {
    public QuickTag(string tag, int index) {
      Tag = tag;
      Selected = false;
      Index = index;
    }

    public string Tag { get; set; }
    public bool Selected { get; set; }
    public int Index { get; set; }
  }
}


