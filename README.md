# OverwriteYamaPlayerPlaylist

## 概要

`YamaPlayer` のプレイリストを、JSON ファイルから一括で上書きする Unity Editor 用ツールです。  
現在ロード中のシーン内にある `YamaPlayer` を一覧表示し、対象を個別に ON/OFF したうえで、各 `PlayListContainer` の既存プレイリストを削除し、JSON の内容で置き換えます。

## できること

- シーン内の `YamaPlayer` を一覧表示し、一括処理
- `YamaPlayer` ごとに更新対象を ON/OFF
- 検索で `Scene`、`Name`、`Hierarchy Path` を絞り込み
- `Name` または `Path` で並び替え
- `All On/Off` と `Filtered On/Off` に対応
- `YamaPlayer` 名と Hierarchy 上のパスを一覧表示
- 一覧で選択した `YamaPlayer` を Hierarchy 上でも選択
- JSON の再読込
- JSON のプレイリスト数、総トラック数、各プレイリスト名の要約表示
- 実行前の対象確認ダイアログ
- 実行後のエラー一覧表示
- 最後に使った JSON パス、検索条件、並び順、対象 ON/OFF 状態を保持
- 既存プレイリストを削除して JSON の内容で置換
- Undo 対応

## 前提

- Unity Editor 上で使うツールです
- 対象は現在ロード中のシーン内にある `YamaPlayer` です
- JSON は `YamaPlayer Playlist Editor` のエクスポート形式を想定しています

## 使い方

1. Unity メニューから `Tools/PoppoWorks/Overwrite YamaPlayer Playlist From Json` を実行します
2. ウィンドウ上部の `Select Json` でインポートしたい JSON ファイルを選択します
3. 必要に応じて `Reload` で同じ JSON を再読込します
4. 検索欄で対象を絞り込みます
5. 一覧で対象にしたい `YamaPlayer` のチェックを ON/OFF します
6. 必要に応じて `All On`、`All Off`、`Filtered On`、`Filtered Off` を使います
7. 一覧上の `Name` または `Hierarchy Path` をクリックすると、その `YamaPlayer` が Hierarchy 上でも選択されます
8. `Overwrite Selected YamaPlayers` を押して実行します

## JSON 形式

想定している JSON は次の形式です。

```json
{
  "playlists": [
    {
      "Active": true,
      "Name": "Playlist A",
      "Tracks": [
        {
          "Mode": 0,
          "Title": "Track 1",
          "Url": "https://example.com/video1"
        }
      ],
      "YoutubeListId": "PLxxxxxxxx"
    }
  ]
}
```

## 動作仕様

- 各 `YamaPlayer` ごとに `PlayListContainer` を検索します
- `PlayListContainer` を持つ `YamaPlayer` だけを一覧表示します
- 検索は `Scene`、`Name`、`Hierarchy Path` を対象にします
- 並び替えは `Name` または `Path` を基準に行います
- 一覧には `Name` と `Hierarchy Path` を表示します
- `PlayListContainer` の子要素 `index 1` 以降を既存プレイリストとして削除します
- JSON 内の `playlists` をもとに新しい `PlayList` オブジェクトを生成します
- `playListName`、`tracks`、`YouTubePlayListID` を設定します
- シーンは Dirty 状態になります
- 最後に使った設定は `EditorPrefs` に保存されます

## 注意点

- 実行すると対象 `YamaPlayer` の既存プレイリストは消えます
- 選択した `YamaPlayer` すべてが同じ内容に上書きされます
- プレハブアセット自体ではなく、ロード中シーン上のインスタンスを更新します
- JSON の内容が不正で `playlists` を読めない場合は処理を中断します
- 一部だけ失敗した場合でも、成功した対象はそのまま更新されます
- 失敗内容はウィンドウ下部と完了ダイアログに表示されます

## 想定用途

- ワールド内に複数配置した `YamaPlayer` に同じプレイリストを配りたいとき
- 別環境で編集したプレイリストを一括反映したいとき
- 既存プレイリストを手作業で複製せずに同期したいとき
