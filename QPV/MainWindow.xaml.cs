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
using Colors;

namespace QPV {

  public class IManager {
    private int currentImg = 0;
    private bool[] loaded;
    private int[] positions;
    List<ImageInfo> images;
    List<int> sequence;
    List<string> folders;
    Dictionary<string, JObject> ratings;
    Dictionary<string, JObject> hashes;
    MainWindow win;
    Random random;
    private int pos = -1;
    private DispatcherTimer starsTimer;
    private int starsToGo;
    private int totalRated;
    public bool needSavingTags;


    public IManager(MainWindow mainWindow) {
      needSavingTags = false;
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
      hashes = new Dictionary<string, JObject>();
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
        if (!hashes.ContainsKey(folders[i]))
          hashes[folders[i]] = new JObject();
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


          json = hashes[path];
          if (json[images[i].ImageName] != null)
            images[i].UpdateHash(json[images[i].ImageName].ToObject<string>());

        } catch (Exception) {
          images[i].Rating = -1;
          images[i].Tags.Clear();
        }
      }

      Randomize();

      // Remove from ratings Json all keys that are no more valid
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
          needSavingTags = true;
        }
      }

      // Remove from hashes Json all keys that are no more valid
      foreach (string path in hashes.Keys) {
        JObject json = hashes[path];
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
          needSavingTags = true;
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
      } catch (Exception e) {
        Uri oUri = new Uri("pack://application:,,,/Images/NotValid.png", UriKind.RelativeOrAbsolute);
        im.Source = BitmapFrame.Create(oUri);
        Trace.Write(e.Message);
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

      if (win.similarActive) {
        win.ShowSelectedSimilarBorder();
        if (images[sequence[pos]].Closest[0] != -1) {
          try {
            BitmapImage bm = new BitmapImage();
            bm.BeginInit();
            bm.UriSource = new Uri(images[images[sequence[pos]].Closest[0]].Path + "\\" + images[images[sequence[pos]].Closest[0]].ImageName);
            bm.EndInit();
            win.C0.Source = bm;
            win.B0.BorderBrush = win.selectedSimilar == 0 ? win.borderImgSel : win.borderImgInv;
            win.R0.Visibility = Visibility.Visible;
            win.P0.Visibility = Visibility.Visible;
            win.R0.Width = 128 - 128 * images[sequence[pos]].Distance[0] / win.maxDistance;
            win.P0.Content = (int)(100 - 100 * images[sequence[pos]].Distance[0] / win.maxDistance) + "%";
          } catch (Exception) {
            Uri oUri = new Uri("pack://application:,,,/Images/NotValid.png", UriKind.RelativeOrAbsolute);
            win.C0.Source = BitmapFrame.Create(oUri);
          }
        }
        else {
          win.C0.Source = null;
          win.B0.BorderBrush = win.borderImgInv;
          win.R0.Visibility = Visibility.Hidden;
          win.P0.Visibility = Visibility.Hidden;
        }

        if (images[sequence[pos]].Closest[1] != -1) {
          try {
            BitmapImage bm = new BitmapImage();
            bm.BeginInit();
            bm.UriSource = new Uri(images[images[sequence[pos]].Closest[1]].Path + "\\" + images[images[sequence[pos]].Closest[1]].ImageName);
            bm.EndInit();
            win.C1.Source = bm;
            win.B1.BorderBrush = win.selectedSimilar == 1 ? win.borderImgSel : win.borderImgInv;
            win.R1.Visibility = Visibility.Visible;
            win.P1.Visibility = Visibility.Visible;
            win.R1.Width = 128 - 128 * images[sequence[pos]].Distance[1] / win.maxDistance;
            win.P1.Content = (int)(100 - 100 * images[sequence[pos]].Distance[1] / win.maxDistance) + "%";
          } catch (Exception) {
            Uri oUri = new Uri("pack://application:,,,/Images/NotValid.png", UriKind.RelativeOrAbsolute);
            win.C1.Source = BitmapFrame.Create(oUri);
          }
        }
        else {
          win.C1.Source = null;
          win.B1.BorderBrush = win.borderImgInv;
          win.R1.Visibility = Visibility.Hidden;
          win.P1.Visibility = Visibility.Hidden;
        }

        if (images[sequence[pos]].Closest[2] != -1) {
          try {
            BitmapImage bm = new BitmapImage();
            bm.BeginInit();
            bm.UriSource = new Uri(images[images[sequence[pos]].Closest[2]].Path + "\\" + images[images[sequence[pos]].Closest[2]].ImageName);
            bm.EndInit();
            win.C2.Source = bm;
            win.B2.BorderBrush = win.selectedSimilar == 2 ? win.borderImgSel : win.borderImgInv;
            win.R2.Visibility = Visibility.Visible;
            win.P2.Visibility = Visibility.Visible;
            win.R2.Width = 128 - 128 * images[sequence[pos]].Distance[2] / win.maxDistance;
            win.P2.Content = (int)(100 - 100 * images[sequence[pos]].Distance[2] / win.maxDistance) + "%";
          } catch (Exception) {
            Uri oUri = new Uri("pack://application:,,,/Images/NotValid.png", UriKind.RelativeOrAbsolute);
            win.C2.Source = BitmapFrame.Create(oUri);
          }
        }
        else {
          win.C2.Source = null;
          win.B2.BorderBrush = win.borderImgInv;
          win.R2.Visibility = Visibility.Hidden;
          win.P2.Visibility = Visibility.Hidden;
        }

        if (images[sequence[pos]].Closest[3] != -1) {
          try {
            BitmapImage bm = new BitmapImage();
            bm.BeginInit();
            bm.UriSource = new Uri(images[images[sequence[pos]].Closest[3]].Path + "\\" + images[images[sequence[pos]].Closest[3]].ImageName);
            bm.EndInit();
            win.C3.Source = bm;
            win.B3.BorderBrush = win.selectedSimilar == 3 ? win.borderImgSel : win.borderImgInv;
            win.R3.Visibility = Visibility.Visible;
            win.P3.Visibility = Visibility.Visible;
            win.R3.Width = 128 - 128 * images[sequence[pos]].Distance[3] / win.maxDistance;
            win.P3.Content = (int)(100 - 100 * images[sequence[pos]].Distance[3] / win.maxDistance) + "%";
          } catch (Exception) {
            Uri oUri = new Uri("pack://application:,,,/Images/NotValid.png", UriKind.RelativeOrAbsolute);
            win.C3.Source = BitmapFrame.Create(oUri);
          }
        }
        else {
          win.C3.Source = null;
          win.B3.BorderBrush = win.borderImgInv;
          win.R3.Visibility = Visibility.Hidden;
          win.P3.Visibility = Visibility.Hidden;
        }
      }
      else {
        win.C0.Source = null;
        win.B0.BorderBrush = win.borderImgInv;
        win.R0.Visibility = Visibility.Hidden;
        win.C1.Source = null;
        win.B1.BorderBrush = win.borderImgInv;
        win.R1.Visibility = Visibility.Hidden;
        win.C2.Source = null;
        win.B2.BorderBrush = win.borderImgInv;
        win.R2.Visibility = Visibility.Hidden;
        win.C3.Source = null;
        win.B3.BorderBrush = win.borderImgInv;
        win.R3.Visibility = Visibility.Hidden;
      }
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
        needSavingTags = true;
      }
      images[sequence[pos]].Rating = rating;
      starsToGo = 32 * rating;
      starsTimer.Start();
    }

    internal void SaveJson() {
      if (needSavingTags) {
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
      if (win.similarUpdated) {
        foreach (string path in ratings.Keys) {
          JObject json = new JObject();
          for (int i = 0; i < images.Count; i++) {
            ImageInfo img = images[i];
            if (img.Path == path && img._hashL != null) {
              json[img.ImageName] = img.SerializeHashes();
            }
          }
          string js = JsonConvert.SerializeObject(json);
          File.Delete(path + "\\hashes.json");
          File.WriteAllText(path + "\\hashes.json", js, System.Text.Encoding.UTF8);
        }
      }
    }

    internal void SetFilter() {
      // Check if we need to check tags
      int i;
      bool checkTags = win.OnlyNoTags.IsChecked ?? false;
      if (!checkTags) {
        for (i = 0; i < win.enabledTags.Count; i++)
          if (win.enabledTags[i].TagMode != 0) {
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

    internal void AddRatingsHashes(string path, string ratingFullName, bool rating) {
      try {
        JObject r = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(ratingFullName));
        if (rating)
          ratings[path] = r;
        else
          hashes[path] = r;
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
      if (needSavingTags)
        SaveJson();

      needSavingTags = false;
      currentImg = 0;
      for (int i = 0; i < 5; i++) {
        loaded[i] = false;
        positions[i] = -1;
      }
      SetImageVisibility(0);
      images.Clear();
      folders.Clear();
      ratings.Clear();
      hashes.Clear();
      sequence.Clear();
      starsTimer.Stop();
      win.Stars.Width = 0;
      win.B0.Visibility = Visibility.Hidden;
      win.B1.Visibility = Visibility.Hidden;
      win.B2.Visibility = Visibility.Hidden;
      win.B3.Visibility = Visibility.Hidden;
      win.C0.Visibility = Visibility.Hidden;
      win.C1.Visibility = Visibility.Hidden;
      win.C2.Visibility = Visibility.Hidden;
      win.C3.Visibility = Visibility.Hidden;
      win.selectedSimilar = -1;
      win.similarActive = false;
      pos = 0;
    }

    internal bool NoImageShown() {
      return pos < 0 || pos >= sequence.Count || sequence[pos] >= images.Count;
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
        if (sequence[i] == imgIndex) {
          seqPos = i;
          break;
        }
      if (seqPos != -1)
        sequence.RemoveAt(seqPos);
    }

    internal void RecalculateDuplicateIndexes(List<DuplicateImage> dups) {
      for (int i = 0; i < dups.Count; i++) {
        for (int j = 0; j < images.Count; j++) {
          string path = images[j].Path + "\\" + images[j].ImageName;
          if (dups[i].Path == path || dups[i].Duplicate == path) {
            dups[i].ImgIndex = j;
            break;
          }
        }
      }
    }


    public class CHTP {
      public bool force; // Force the recalculation
      public int[] set; // Small vector of image indexes to process
      public int len; // Lenght of the valid items in the set
      public int prog; // Progress done on the set
      public int pixelOffset; // Used to define how broad the checking of distances should be
      public bool needMore; // Set to True by the task when the set is completed

      public ManualResetEventSlim toWaitOn;

      public CHTP(bool f) {
        set = new int[16];
        len = 0;
        prog = 0;
        force = f;
        pixelOffset = 0;
        needMore = true;
        toWaitOn = new ManualResetEventSlim(false);
      }
    };

    public void CalculateHashThread(object parameters) {
      CHTP pars = (CHTP)parameters;

      while (true) {
        pars.toWaitOn.Wait();
        pars.needMore = false;
        for (int i = 0; i < pars.len; i++) {
          if (images[pars.set[i]].CalculateHash(pars.force, win.blurDiff))
            win.similarUpdated = true;
          Thread.Sleep(10);
          pars.prog++;
        }
        pars.needMore = true;
        GC.Collect();
        pars.toWaitOn.Reset();
      }
    }

    public void CalculateDistancesThread(object parameters) {
      CHTP pars = (CHTP)parameters;

      while (true) {
        pars.toWaitOn.Wait();
        pars.needMore = false;
        for (int i = 0; i < pars.len; i++) {
          for (int j = pars.set[i] + 1; j < images.Count; j++) {
            if (pars.set[i] == j) continue;
            double distance = CalculateDistance(images[pars.set[i]], images[j], win.lumaWeight, win.chromaWeight, pars.pixelOffset);
            if (distance > win.maxDistance) continue; // Too far
            images[pars.set[i]].SetDistance(distance, j);
            images[j].SetDistance(distance, pars.set[i]);
          }
          Thread.Sleep(10);
          pars.prog++;
        }
        pars.needMore = true;
        pars.toWaitOn.Reset();
      }
    }

    internal void CalculateHash(bool force, int pixelOffset) {
      win.PBar.Visibility = Visibility.Visible;
      win.PBar.Maximum = images.Count;
      win.PBar.Value = 0;
      Stopwatch watch = Stopwatch.StartNew();

      // Start the threads
      int numThreads = 6;
      Thread[] threads = new Thread[numThreads];
      CHTP[] chtps = new CHTP[numThreads];
      for (int i = 0; i < numThreads; i++) {
        threads[i] = new Thread(new ParameterizedThreadStart(CalculateHashThread));
        chtps[i] = new CHTP(force);
        threads[i].Start(chtps[i]);
      }


      int secs = 0;
      int mins = 0;
      int hours = 0;

      win.Title = "Calculating hashes...";
      int done = 0;
      int start = 0;
      while (done < images.Count) {
        // Update the value of the progress with all the progresses of the threads
        done = 0;
        for (int i = 0; i < numThreads; i++) {
          done += chtps[i].prog;
        }

        // Update the progress
        long elapsedMs = watch.ElapsedMilliseconds;
        double remaining = 1.0 * (images.Count - done) * elapsedMs / (done == 0 ? 1 : done);
        secs = (int)remaining / 1000 % 60;
        mins = ((int)remaining / 1000) / 60 % 60;
        hours = ((int)remaining / 3600000);

        win.PBar.Dispatcher.Invoke(new Action(() => {
          win.PBar.Value = done;
          win.Title = "Calculating hashes... (" + done + "/" + images.Count + ")  ETA " + hours + ":" + (mins < 10 ? "0" : "") + mins + ":" + (secs < 10 ? "0" : "") + secs;
        }), DispatcherPriority.ContextIdle);

        // Find which thread need some data and feed them with data
        for (int i = 0; i < numThreads; i++) {
          if (chtps[i].needMore) {
            chtps[i].len = 0;
            for (int j = 0; j < 16; j++) {
              if (j + start < images.Count) {
                chtps[i].set[j] = start + j;
                chtps[i].len++;
              }
              else break;
            }
            if (chtps[i].len > 0) {
              chtps[i].toWaitOn.Set();
              start += chtps[i].len;
            }
          }
        }

        Thread.Sleep(1500);
      }

      bool completed = false;
      while (!completed) {
        completed = true;
        for(int i=0; i<numThreads; i++)
          if (!chtps[i].needMore) {
            completed = false;
            break;
          }
        Thread.Sleep(1000);
      }
      for (int i = 0; i < numThreads; i++)
        threads[i].Abort();

      watch.Stop();
      long hashTime = watch.ElapsedMilliseconds;

      for (int i = 0; i < images.Count; i++)
        for (int j = 0; j < 4; j++)
          images[i].Distance[j] = 1000000000;

      win.Title = "Finding closest images";
      for (int i = 0; i < numThreads; i++) {
        threads[i] = new Thread(new ParameterizedThreadStart(CalculateDistancesThread));
        chtps[i].len = 0;
        chtps[i].prog = 0;
        chtps[i].pixelOffset = pixelOffset;
        chtps[i].needMore = true;
        threads[i].Start(chtps[i]);
      }

      win.PBar.Value = 0;
      Thread.Sleep(25);
      watch.Restart();

      done = 0;
      start = 0;
      while (done < images.Count) {
        // Update the value of the progress with all the progresses of the threads
        done = 0;
        for (int i = 0; i < numThreads; i++) {
          done += chtps[i].prog;
        }

        // Update the progress
        long elapsedMs = watch.ElapsedMilliseconds;
        double remaining = 1.0 * (images.Count - done) * elapsedMs / (done == 0 ? 1 : done);
        secs = (int)remaining / 1000 % 60;
        mins = ((int)remaining / 1000) / 60 % 60;
        hours = ((int)remaining / 3600000);

        win.PBar.Dispatcher.Invoke(new Action(() => {
          win.PBar.Value = done;
          win.Title = "Finding closest images... (" + done + "/" + images.Count + ")  ETA " + hours + ":" + (mins < 10 ? "0" : "") + mins + ":" + (secs < 10 ? "0" : "") + secs;
        }), DispatcherPriority.ContextIdle);

        // Find which thread need some data and feed them with data
        for (int i = 0; i < numThreads; i++) {
          if (chtps[i].needMore) {
            chtps[i].len = 0;
            for (int j = 0; j < 16; j++) {
              if (j + start < images.Count) {
                chtps[i].set[j] = start + j;
                chtps[i].len++;
              }
              else break;
            }
            if (chtps[i].len > 0) {
              chtps[i].toWaitOn.Set();
              start += chtps[i].len;
            }
          }
        }

        Thread.Sleep(1500);
      }



      secs = (int)hashTime / 1000 % 60;
      mins = ((int)hashTime / 1000) / 60 % 60;
      hours = ((int)hashTime / 3600000);
      string hashTimeS = hours + ":" + (mins < 10 ? "0" : "") + mins + ":" + (secs < 10 ? "0" : "") + secs;
      secs = (int)watch.ElapsedMilliseconds / 1000 % 60;
      mins = ((int)watch.ElapsedMilliseconds / 1000) / 60 % 60;
      hours = ((int)watch.ElapsedMilliseconds / 3600000);

      win.Title = "QPV Total time: Hashes = " + hashTimeS + " Distance = " + hours + ":" + (mins < 10 ? "0" : "") + mins + ":" + (secs < 10 ? "0" : "") + secs;
      watch.Stop();

      completed = false;
      while (!completed) {
        completed = true;
        for (int i = 0; i < numThreads; i++)
          if (!chtps[i].needMore) {
            completed = false;
            break;
          }
        Thread.Sleep(1000);
      }
      for (int i = 0; i < numThreads; i++)
        threads[i].Abort();

      win.PBar.Visibility = Visibility.Hidden;
    }

    private double CalculateDistance(ImageInfo src, ImageInfo dst, double lumaWeight, double chromaWeight, int pixelOffset) {
      if (src._hashL == null || dst._hashL == null || src._hashC == null || dst._hashC == null)
        return 1000000000.0;

      // Go from 16x16 and reduce to 14x14 (pixel movement up to 4 pixels)
      // Find the closest euclidean distance inside the block
      // Calculate the best distance, but increase the distance in case the areas are smaller
      // Return the best (minor) distance

      double bestDistance = 1000000000.0;
      for (int sbsx = 16; sbsx > 15 - pixelOffset; sbsx--)
        for (int sbsy = 16; sbsy > 15 - pixelOffset; sbsy--) {

          // Where the sub-block should start and how many steps we can check?
          int stepsx = 17 - sbsx;
          int stepsy = 17 - sbsy;

          for (int stepx = 0; stepx < stepsx; stepx++)
            for (int stepy = 0; stepy < stepsy; stepy++) {
              // The block will be (stepx, stepy)-(stepx+sbsx, stepy+sbsy)


              // We need to calculate the euclidean distance in the same sub-region and get the lower distance (+256*area of block)
              for (int dstepx = 0; dstepx < stepsx; dstepx++)
                for (int dstepy = 0; dstepy < stepsy; dstepy++) {

                  double distanceL = 0;
                  double distanceC = 0;
                  for (int x = 0; x < sbsx; x++)
                    for (int y = 0; y < sbsy; y++) {
                      double pointL = src._hashL[stepx + x + 16 * (stepy + y)] - dst._hashL[dstepx + x + 16 * (dstepy + y)];
                      double pointC = src._hashC[stepx + x + 16 * (stepy + y)] - dst._hashC[dstepx + x + 16 * (dstepy + y)];
                      distanceL += pointL * pointL;
                      distanceC += pointC * pointC;
                    }
                  // Normalize by the area of the block
                  distanceL += 64 * 64 * (256 - sbsx * sbsy);
                  distanceC += 64 * 64 * (256 - sbsx * sbsy);
                  distanceL = Math.Sqrt(distanceL) / 256;
                  distanceC = Math.Sqrt(distanceC) / 256;

                  double distance = lumaWeight * distanceL + chromaWeight * distanceC;
                  if (bestDistance > distance)
                    bestDistance = distance;
                }
            }

        }

      return bestDistance;
    }

    void CalculateHistograms(Image h, ImageInfo src, ImageInfo dst) {
      DrawingVisual drawingVisual = new DrawingVisual();
      DrawingContext drawingContext = drawingVisual.RenderOpen();

      Pen blue = new Pen(Brushes.Blue, 1);
      Pen green = new Pen(Brushes.LawnGreen, 1);

      byte[] diffsL = new byte[256];
      byte[] diffsC = new byte[256];
      for (int i = 0; i < 256; i++) {
        diffsL[i] = (byte)Math.Abs(src._hashL[i] - dst._hashL[i]);
        diffsC[i] = (byte)Math.Abs(src._hashC[i] - dst._hashC[i]);

        int x = i % 16;
        int y = i / 16;

        SolidColorBrush scb = new SolidColorBrush(Color.FromArgb(255, (byte)(255 - diffsL[i]), (byte)(255 - diffsC[i]), 128));

        drawingContext.DrawRectangle(
          scb,
          new Pen(scb, 1),
          new Rect(new Point(4 * x, 4 * y), new Point(4 * x + 3, 4 * y + 3))
        );
      }
      drawingContext.Close();

      RenderTargetBitmap bmp = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
      bmp.Render(drawingVisual);

      h.Source = bmp;
    }

    internal void ShowMostSimilarImage() {
      if (win.selectedSimilar == -1 || !win.similarActive) return;
      ImageInfo img = GetImageInfo();
      if (img.Closest[win.selectedSimilar] != -1) {
        for (int i = 0; i < sequence.Count; i++) {
          if (sequence[i] == img.Closest[win.selectedSimilar]) {
            pos = i;
            ShowImg(ShowMode.Same);
            win.selectedSimilar = -1;
            win.B0.BorderBrush = win.borderImgInv;
            win.B1.BorderBrush = win.borderImgInv;
            win.B2.BorderBrush = win.borderImgInv;
            win.B3.BorderBrush = win.borderImgInv;
            return;
          }
        }
      }
    }

    internal ImageInfo GetImageInfo(int v) {
      if (v == -1) return null;
      return images[v];
    }
  }

  public enum Mode {
    Normal = 0, // No automatic time, Left, Right will change the image randomly [F1]
    Slide = 1, // Automatic progress of slide show, Left/Right will still work  [F2]
    RatingsTagging = 2, // Define Tags and Rating of the image [F3]
    FilterSort = 3, // Filters the images, Sort and Randomize the images [F4]
    TagDefs = 5, // Define the list of Tags [F5]
    ScanImages = 6 // Scan the loaded images to find similar images
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
    public int selectedSimilar = -1;
    public bool similarActive = false;
    public bool similarUpdated = false;

    // FIXME

    // If any of the hashes is updated make it true. When closing, find for all paths, all the images of each path, then create a json with the values. On loading a folder try to check if the Json is there, in case load it and manually update the hashes of the images
    public double lumaWeight;
    public double chromaWeight;
    public double maxDistance;
    public double blurDiff;


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
        if (!im.HasFiles()) return;
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
      else if (e.Key == Key.F6) {
        if (!im.HasFiles()) return;
        timer.Stop();
        dgTags.Visibility = Visibility.Hidden;
        Stars.Width = 0;
        prevMode = (mode == Mode.Slide ? Mode.Slide : Mode.Normal);
        mode = Mode.ScanImages;
        Title = "QPV Scanning images";
        MakeTagCloseButtonAvailable(false);
        ShowFilters(false);
        ShowSimilarityParameters(true);
        return;
      }
      else if (e.Key == Key.F7) {
        QPV2.WinImg wi = new QPV2.WinImg();
        wi.Show();
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
        else if (e.Key == Key.PageUp && similarActive) {
          ImageInfo img = im.GetImageInfo();
          selectedSimilar--;
          if (selectedSimilar == -1) selectedSimilar = 3;
          if (img.Closest[selectedSimilar] == -1) {
            if (selectedSimilar == 3) {
              if (img.Closest[2] != -1)
                selectedSimilar = 2;
              else if (img.Closest[1] != -1)
                selectedSimilar = 1;
              else if (img.Closest[0] != -1)
                selectedSimilar = 0;
              else
                selectedSimilar = -1;
            }
            else if (selectedSimilar == 2) {
              if (img.Closest[1] != -1)
                selectedSimilar = 1;
              else if (img.Closest[0] != -1)
                selectedSimilar = 0;
              else
                selectedSimilar = -1;
            }
            else if (selectedSimilar == 1) {
              if (img.Closest[0] != -1)
                selectedSimilar = 0;
              else
                selectedSimilar = -1;
            }
          }
          ShowSelectedSimilarBorder();
        }
        else if (e.Key == Key.PageDown && similarActive) {
          ImageInfo img = im.GetImageInfo();
          selectedSimilar++;
          if (selectedSimilar == 4) selectedSimilar = 0;
          if (img.Closest[selectedSimilar] == -1) {
            if (img.Closest[0] != -1)
              selectedSimilar = 0;
            else
              selectedSimilar = -1;
          }
          ShowSelectedSimilarBorder();
        }
        else if (e.Key == Key.Enter && similarActive)
          im.ShowMostSimilarImage();
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
          e.Handled = true;
          return;
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

        if (quickTag != -1 && e.Key == Key.Q) {
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
                im.needSavingTags = true;

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

    public void ShowSelectedSimilarBorder() {
      B0.BorderBrush = selectedSimilar == 0 ? borderImgSel : borderImgInv;
      B1.BorderBrush = selectedSimilar == 1 ? borderImgSel : borderImgInv;
      B2.BorderBrush = selectedSimilar == 2 ? borderImgSel : borderImgInv;
      B3.BorderBrush = selectedSimilar == 3 ? borderImgSel : borderImgInv;

      ImageInfo img = im.GetImageInfo();
      if (img == null || img.bmpL == null || img.bmpC == null) return;
      using (MemoryStream memory = new MemoryStream()) {
        img.bmpL.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
        memory.Position = 0;
        BitmapImage bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        SrcL.Source = bitmapImage;
      }
      using (MemoryStream memory = new MemoryStream()) {
        img.bmpC.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
        memory.Position = 0;
        BitmapImage bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        SrcC.Source = bitmapImage;
      }

      if (selectedSimilar == -1) {
        DstL.Source = null;
        DstC.Source = null;
        return;
      }

      ImageInfo dst = im.GetImageInfo(img.Closest[selectedSimilar]);
      if (dst == null || dst.bmpL == null || dst.bmpC == null) return;
      using (MemoryStream memory = new MemoryStream()) {
        dst.bmpL.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
        memory.Position = 0;
        BitmapImage bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        DstL.Source = bitmapImage;
      }
      using (MemoryStream memory = new MemoryStream()) {
        dst.bmpC.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
        memory.Position = 0;
        BitmapImage bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        DstC.Source = bitmapImage;
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
      Invert.Visibility = v;
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
        switch (chosenFilter) {
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
    public SolidColorBrush borderImgSel = new SolidColorBrush() {
      Color = Color.FromArgb(255, 255, 0, 0)
    };
    public SolidColorBrush borderImgInv = new SolidColorBrush() {
      Color = Color.FromArgb(0, 0, 0, 0)
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

      Title = "QPV Finding files...";

      // Get all image files in the directory, get also the json ratings if any
      string[] allfiles = Directory.GetFiles(d.FileName, "*.*", (recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
      if (replace) {
        Title = "QPV Loading Images" + (recursive ? " (Recursive)..." : "...");
        im.CleanUp();
      }
      else
        Title = "QPV Adding Images" + (recursive ? " (Recursive)..." : "...");

      PBar.Visibility = Visibility.Visible;
      PBar.Minimum = 0;
      PBar.Maximum = allfiles.Length;
      PBar.Value = 0;
      int done = 0;
      foreach (var file in allfiles) {
        FileInfo info = new FileInfo(file);
        done++;
        string ext = info.Extension.ToLowerInvariant();
        if (ext.Length < 2) continue; // Not interesting
        ext = ext.Substring(1);

        if (info.Name.ToLowerInvariant() == "ratings.json") {
          im.AddRatingsHashes(info.DirectoryName, info.FullName, true);
          continue;
        }
        if (info.Name.ToLowerInvariant() == "hashes.json") {
          im.AddRatingsHashes(info.DirectoryName, info.FullName, false);
          continue;
        }

        if (ext != "jpg" && ext != "jpeg" && ext != "gif" && ext != "png") continue; // Not interesting

        // Add to the list and check for the ratings
        im.Add(info.DirectoryName, info.Name);

        if (done % 11 == 0)
          PBar.Dispatcher.Invoke(new Action(() => {
            PBar.Value = done;
          }), DispatcherPriority.ContextIdle);
      }

      im.Complete();
      PBar.Visibility = Visibility.Hidden;

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

      for (int i = 0; i < quickTags.Count; i++)
        if (quickTags[i].Index == td.ID) {
          quickTags.RemoveAt(i);
          break;
        }
      QuickTags.Dispatcher.Invoke(new Action(() => {
        QuickTags.ItemsSource = null;
        QuickTags.ItemsSource = quickTags;
      }), DispatcherPriority.ContextIdle);

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

        // Rename also the quicktags
        for (int i = 0; i < quickTags.Count; i++)
          if (quickTags[i].Index == td.ID) {
            quickTags[i].Tag = td.TagName;
            break;
          }
        QuickTags.Dispatcher.Invoke(new Action(() => {
          QuickTags.ItemsSource = null;
          QuickTags.ItemsSource = quickTags;
        }), DispatcherPriority.ContextIdle);

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
      im.needSavingTags = true;
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

    private void OnInvertTags(object sender, RoutedEventArgs e) {
      foreach (TagDef td in enabledTags)
        if (td.TagMode == 0)
          td.TagMode = 1;
        else
          td.TagMode = 0;
      UpdateEnabledTagsGridThread();
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

    private void OnFindSimilarImages(object sender, RoutedEventArgs e) {
      ShowSimilarityParameters(false);

      if (im.GetImagesCount() > 1000) {
        if (MessageBox.Show("Warning\nYou have more than " + im.GetImagesCount() + " images.\nThe scan may take a very long time.\n\nDo you want to continue?", "QPV", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
          return;
      }
      Title = "QPV Scanning images";
      Thread.Sleep(60);

      // Do the actual scan
      Title = "QPV Scanning images and calculating hashes...";
      im.CalculateHash(Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) || sender == FFindSimilarImagesButton, UsePixelOffsets.SelectedIndex);
      selectedSimilar = -1;
      similarActive = true;
      mode = prevMode;
// FIXME      Title = "QPV Scanning of images completed";
      B0.Visibility = Visibility.Visible;
      B1.Visibility = Visibility.Visible;
      B2.Visibility = Visibility.Visible;
      B3.Visibility = Visibility.Visible;
      C0.Visibility = Visibility.Visible;
      C1.Visibility = Visibility.Visible;
      C2.Visibility = Visibility.Visible;
      C3.Visibility = Visibility.Visible;
    }

    private void ShowSimilarityParameters(bool show) {
      Visibility v = show ? Visibility.Visible : Visibility.Hidden;
      LLumaWeight.Visibility = v;
      LChromaWeight.Visibility = v;
      LMaxDistance.Visibility = v;
      ChromaWeight.Visibility = v;
      LumaWeight.Visibility = v;
      MaxDistance.Visibility = v;
      LBlurDiff.Visibility = v;
      BlurDiff.Visibility = v;
      FindSimilarImagesButton.Visibility = v;
      FFindSimilarImagesButton.Visibility = v;
      UsePixelOffsets.Visibility = v;
    }

    private void OnSliderUpdated(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (sender == LumaWeight) {
        lumaWeight = LumaWeight.Value;
        LLumaWeight.Content = "Luma Weight: " + lumaWeight;
      }
      else if (sender == ChromaWeight) {
        chromaWeight = ChromaWeight.Value;
        LChromaWeight.Content = "Chroma Weight: " + chromaWeight;
      }
      if (sender == MaxDistance) {
        maxDistance = MaxDistance.Value;
        LMaxDistance.Content = "Max Distance: " + maxDistance;
      }
      if (sender == BlurDiff) {
        blurDiff = BlurDiff.Value;
        LBlurDiff.Content = "Blur Threshold: " + blurDiff;
      }
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

    public System.Drawing.Bitmap bmpL; // FIXME remove in future
    public System.Drawing.Bitmap bmpC; // FIXME remove in future
    public byte[] _hashL;
    public byte[] _hashC;
    public int[] Closest;
    public int[] Distance;
    private readonly object syncLock;

    public ImageInfo(string path, string name, int rating) {
      ImageName = name;
      Rating = rating;
      Path = path;
      Tags = new HashSet<int>();
      Size = 0;

      Closest = new int[4];
      Closest[0] = Closest[1] = Closest[2] = Closest[3] = -1;
      Distance = new int[4];
      Distance[0] = Distance[1] = Distance[2] = Distance[3] = 1000000000;
      syncLock = new object();
  }

    public bool CalculateHash(bool force, double blurDiff) {
      if (_hashL != null && _hashC != null && !force)
        return false;

      ImageMatcher.CalculateHash(this, Path + "\\" + ImageName, blurDiff);
      if (_hashL == null || _hashC == null)
        return false;

      bmpL = new System.Drawing.Bitmap(16, 16);
      for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
          bmpL.SetPixel(x, y, System.Drawing.Color.FromArgb(255, _hashL[x+16*y], _hashL[x+16*y], _hashL[x+16*y]));

      bmpC = new System.Drawing.Bitmap(16, 16);
      for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++) {
          if (_hashC[x + 16 * y] == 0) {
            bmpC.SetPixel(x, y, System.Drawing.Color.FromArgb(255, 0, 0, 0));
          } else {
            RGB rgb = Colors.Colors.HSLtoRGB(360.0 * (_hashC[x + 16 * y] - 1) / 254.0, 1.0, 0.9);
            bmpC.SetPixel(x, y, System.Drawing.Color.FromArgb(255, rgb.Red, rgb.Green, rgb.Blue));
          }
        }

      return true;
    }

    static string hashSerializer = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ01234567890!@#$%^&*()[]{};:'`~,./<>?|¡¢£¤¥¦§©ª«¬­®¯°±²³µ¶¹º»¼½¾¿ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿĀāĂăĄąĆćĈĉĊċČčĎďĐđĒēĔĕĖėĘęĚěĜĝĞğĠġĢģĤĥĦħĨĩĪīĬĭĮįİıĲĳĴĵĶķĸĹĺĻļĽľĿŀŁłŃńŅņŇňŉŋŘ";

    internal string SerializeHashes() {
      string res = "";
      if (_hashL == null || _hashC == null) return "";
      for (int i = 0; i < 256; i++) {
        res += hashSerializer.Substring((int)_hashL[i], 1);
      }
      res += " ";
      for (int i = 0; i < 256; i++) {
        res += hashSerializer.Substring((int)_hashC[i], 1);
      }
      return res;
    }

    internal void UpdateHash(string hash) {
      if (_hashL == null) _hashL = new byte[256];
      if (_hashC == null) _hashC = new byte[256];
      for (int i = 0; i < 256; i++) {
        _hashL[i] = (byte)hashSerializer.IndexOf(hash.Substring(i, 1));
        _hashC[i] = (byte)hashSerializer.IndexOf(hash.Substring(257 + i, 1));
      }

      bmpL = new System.Drawing.Bitmap(16, 16);
      for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
          bmpL.SetPixel(x, y, System.Drawing.Color.FromArgb(255, _hashL[x + 16 * y], _hashL[x + 16 * y], _hashL[x + 16 * y]));

      bmpC = new System.Drawing.Bitmap(16, 16);
      for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++) {
          if (_hashC[x + 16 * y] == 0) {
            bmpC.SetPixel(x, y, System.Drawing.Color.FromArgb(255, 0, 0, 0));
          }
          else {
            RGB rgb = Colors.Colors.HSLtoRGB(360.0 * (_hashC[x + 16 * y] - 1) / 254.0, 1.0, 0.9);
            bmpC.SetPixel(x, y, System.Drawing.Color.FromArgb(255, rgb.Red, rgb.Green, rgb.Blue));
          }
        }
    }

    internal bool SetDistance(double distance, int dst) {
      if (distance == 1000000000) return false;

      lock (syncLock) {
        int dd = (int)distance;
        if (dd > Distance[3]) return false;
        for (int pos = 0; pos < 4; pos++) {
          if (dd < Distance[pos]) {
            for (int i = 3; i > pos; i--) {
              Distance[i] = Distance[i - 1];
              Closest[i] = Closest[i - 1];
            }
            Distance[pos] = dd;
            Closest[pos] = dst;
            return true;
          }
        }
        return false;
      }
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





namespace Colors {
  public struct RGB {
    public static readonly RGB Empty = new RGB();

    private int red;
    private int green;
    private int blue;

    public static bool operator ==(RGB item1, RGB item2) {
      return (
          item1.Red == item2.Red
          && item1.Green == item2.Green
          && item1.Blue == item2.Blue
          );
    }

    public static bool operator !=(RGB item1, RGB item2) {
      return (
          item1.Red != item2.Red
          || item1.Green != item2.Green
          || item1.Blue != item2.Blue
          );
    }

    public int Red {
      get { return red; }
      set { red = (value > 255) ? 255 : ((value < 0) ? 0 : value); }
    }

    public int Green {
      get { return green; }
      set { green = (value > 255) ? 255 : ((value < 0) ? 0 : value); }
    }

    public int Blue {
      get { return blue; }
      set { blue = (value > 255) ? 255 : ((value < 0) ? 0 : value); }
    }

    public RGB(int R, int G, int B) {
      this.red = (R > 255) ? 255 : ((R < 0) ? 0 : R);
      this.green = (G > 255) ? 255 : ((G < 0) ? 0 : G);
      this.blue = (B > 255) ? 255 : ((B < 0) ? 0 : B);
    }

    public override bool Equals(Object obj) {
      if (obj == null || GetType() != obj.GetType()) return false;
      return (this == (RGB)obj);
    }

    public override int GetHashCode() {
      return Red.GetHashCode() ^ Green.GetHashCode() ^ Blue.GetHashCode();
    }
  }

  public struct HSL {
    public static readonly HSL Empty = new HSL();

    private double hue;
    private double saturation;
    private double luminance;

    public static bool operator ==(HSL item1, HSL item2) {
      return (item1.Hue == item2.Hue && item1.Saturation == item2.Saturation && item1.Luminance == item2.Luminance);
    }

    public static bool operator !=(HSL item1, HSL item2) {
      return (item1.Hue != item2.Hue || item1.Saturation != item2.Saturation || item1.Luminance != item2.Luminance);
    }

    public double Hue {
      get { return hue; }
      set { hue = (value > 360) ? 360 : ((value < 0) ? 0 : value); }
    }

    public double Saturation {
      get { return saturation; }
      set { saturation = (value > 1) ? 1 : ((value < 0) ? 0 : value); }
    }

    public double Luminance {
      get { return luminance; }
      set { luminance = (value > 1) ? 1 : ((value < 0) ? 0 : value); }
    }

    public HSL(double h, double s, double l) {
      hue = (h > 360) ? 360 : ((h < 0) ? 0 : h);
      saturation = (s > 1) ? 1 : ((s < 0) ? 0 : s);
      luminance = (l > 1) ? 1 : ((l < 0) ? 0 : l);
    }

    public override bool Equals(Object obj) {
      if (obj == null || GetType() != obj.GetType()) return false;
      return (this == (HSL)obj);
    }

    public override int GetHashCode() {
      return Hue.GetHashCode() ^ Saturation.GetHashCode() ^ Luminance.GetHashCode();
    }
  }

  class Colors {
    public static HSL RGBtoHSL(int red, int green, int blue) {
      double h = 0, s = 0, l = 0;
      double r = (double)red / 255.0;
      double g = (double)green / 255.0;
      double b = (double)blue / 255.0;
      double max = Math.Max(r, Math.Max(g, b));
      double min = Math.Min(r, Math.Min(g, b));

      // hue
      if (max == min) { h = 0; } // undefined 
      else if (max == r && g >= b) { h = 60.0 * (g - b) / (max - min); }
      else if (max == r && g < b) { h = 60.0 * (g - b) / (max - min) + 360.0; }
      else if (max == g) { h = 60.0 * (b - r) / (max - min) + 120.0; }
      else if (max == b) { h = 60.0 * (r - g) / (max - min) + 240.0; }

      // luminance
      l = (max + min) / 2.0;

      // saturation
      if (l == 0 || max == min) { s = 0; }
      else if (0 < l && l <= 0.5) { s = (max - min) / (max + min); }
      else if (l > 0.5) { s = (max - min) / (2 - (max + min)); } //(max-min > 0)?

      return new HSL(Double.Parse(String.Format("{0:0.##}", h)), Double.Parse(String.Format("{0:0.##}", s)), Double.Parse(String.Format("{0:0.##}", l)));
    }

    public static RGB HSLtoRGB(double h, double s, double l) {
      if (s == 0) { // achromatic color (gray scale)
        return new RGB(Convert.ToInt32(Double.Parse(String.Format("{0:0.00}", l * 255.0))), Convert.ToInt32(Double.Parse(String.Format("{0:0.00}", l * 255.0))), Convert.ToInt32(Double.Parse(String.Format("{0:0.00}", l * 255.0))));
      }
      else {
        double q = (l < 0.5) ? (l * (1.0 + s)) : (l + s - (l * s));
        double p = (2.0 * l) - q;

        double Hk = h / 360.0;
        double[] T = new double[3];
        T[0] = Hk + (1.0 / 3.0);    // Tr
        T[1] = Hk;                // Tb
        T[2] = Hk - (1.0 / 3.0);    // Tg

        for (int i = 0; i < 3; i++) {
          if (T[i] < 0) T[i] += 1.0;
          if (T[i] > 1) T[i] -= 1.0;

          if ((T[i] * 6) < 1) {
            T[i] = p + ((q - p) * 6.0 * T[i]);
          }
          else if ((T[i] * 2.0) < 1) //(1.0/6.0)<=T[i] && T[i]<0.5
          {
            T[i] = q;
          }
          else if ((T[i] * 3.0) < 2) // 0.5<=T[i] && T[i]<(2.0/3.0)
          {
            T[i] = p + (q - p) * ((2.0 / 3.0) - T[i]) * 6.0;
          }
          else T[i] = p;
        }

        return new RGB(Convert.ToInt32(Double.Parse(String.Format("{0:0.00}", T[0] * 255.0))), Convert.ToInt32(Double.Parse(String.Format("{0:0.00}", T[1] * 255.0))), Convert.ToInt32(Double.Parse(String.Format("{0:0.00}", T[2] * 255.0))));
      }
    }
  }
}