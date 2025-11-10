[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Status: Experimental](https://img.shields.io/badge/status-experimental-orange)

<p align="center">
  <img src="Assets/logo.svg" alt="rinne logo" width="30%" />
</p>

<h1 align="center">RINNE CLI</h1>

<p align="center">
  Rinne はプロジェクトフォルダを差分ではなく完全スナップショットとして扱う履歴管理アプリケーションです。<br/>  
  スナップショットとメタ情報を履歴として蓄積し、任意時点へ復元・合成・再構成します。 
</p>
<br/>

[>English version](./README.en.md)

---

## 概要

Rinne は、ファイルやプロジェクトの「その時点の状態」を保存する
スナップショット型の履歴管理ツールです。
過去の状態を再生・検証・再構成し、再利用を容易にする仕組みを提供します。

---

## Features（予定）

数十GB規模以上の大容量バイナリ資産を主対象として、
フルスナップショットを主軸にしつつ、以下の機能を段階的に試験予定です。

- **compress**：既存スナップショットの圧縮（実験的）
- **deep archive**：チャンク共有＋圧縮による長期保管（実験的）
- **内部構造の刷新**：将来の拡張に備えた再編（実験的）

---

## 思想

変化を追うのではなく、状態そのものを残して再生することを目的とします。
履歴を差分ではなく“完成した状態”として扱い、
可逆で単純、そして有限に循環する設計を志向します。

### 1. 可逆性
すべての履歴は単体で完全に復元可能です。
過去を巻き戻すのではなく、特定時点を再生成します。

### 2. 単純性
特別な仕組みに依存しません。
OS 標準の操作が可能であり、他ツールとの連携も容易です。

### 3. 有限な循環
すべての履歴を保持し続けることは、多くの場面で過剰です。
整理と統合を前提に、本当に必要な状態だけを残し、人が直感的に扱える空間とします。

---

## ディレクトリ構成

```
.rinne/
 ├─ config/                                       # 設定ファイル
 ├─ data/
 │   ├─ main/                                     # 既定の作業空間(space)
 │   │   ├─ 00000001_20251024T091530123.zip       # 履歴データ
 │   │   └─ meta/
 │   │   　  └─ 00000001_20251024T091620223.json  # 履歴メタデータ
 │   └─ other.../                                 # 他の作業空間(space)
 ├─ logs/                                         # 出力ログ
 ├─ state/                                        # 状態
 │   └─ current                                   # 現在選択中の space 名           
 └─ temp/                                         # 一時ファイル格納

.rinneignore                                      # 無視ルール一覧
```

---

## 主なコマンド

### init — 初期化
```text
rinne init
```
現在のディレクトリに `.rinne/` 標準構造を生成します。

---

### save — 保存
```text
rinne save [-s|--space <name>] [-m|--message "<text>"]
```
現在の作業ツリーを `.rinne/data/<space>/` にスナップショットを保存します。  
同時にメタデータ (`meta/<id>.json`) も出力します。

---

### space — 作業空間管理
```text
rinne space current                   #現在選択中のspace名
rinne space list                      #space名一覧
rinne space select <name> [--create]  #spaceを選択
rinne space create <name>             #新spaceの作成
rinne space rename <old> <new>        #space名の変更
rinne space delete <name> [--force]   #spaceの削除
```
独立した保存空間であるspaceを操作します。

---

### verify — 検証
```text
rinne verify [--space <name>] [--meta <path>]
```
メタ情報とハッシュチェーンの整合性を検証します。

---

### restore — 復元
```text
rinne restore <space> <id>
```
指定スナップショットを展開し、プロジェクトを当時の状態に復元します。

---

### diff — 差分表示
```text
rinne diff <id1> <id2> [space]
```
2つのスナップショット間でフォルダ構成やファイル内容の差分を比較します。
結果は標準出力に整形して表示されます。

---

### textdiff — テキスト差分表示
```text
rinne textdiff [<old_id> <new_id> [space]]
```
テキストファイルの差分のみを抽出して表示します。
コードや設定ファイルの比較などに使います。

---

### log — スペース履歴表示
```text
rinne log [space]
```
指定スペース（または現在のスペース）の履歴を一覧表示します。

---

### show — メタ情報表示
```text
rinne show <id> [space]
```
指定スナップショットのメタ情報 (meta.json) を整形表示します。
チェーンハッシュや保存日時などの詳細を確認できます。

---

### recompose — 再構成（履歴合成）
```text
rinne recompose <outspace> <space1> <id1> , <space2> <id2> , ...
```
複数スナップショットを左からの優先順で合成して新しい状態を生成し、指定の space に保存します。

---

### backup — バックアップの作成
```text
rinne backup <outputdir>
```
`.rinne` フォルダ全体をバックアップとして保存します。   

---

### import — 他のリポジトリから space を取り込む
```text
rinne import <source_root> <space> [--mode fail|rename|clean]
```
他の外部 `.rinne` リポジトリから指定した space を現在のリポジトリに取り込みます。  
`--mode` で衝突時の挙動を指定できます。  

| モード | 説明 |
|--------|------|
| fail   | 既定。既存の space があれば中止します。 |
| rename | 重複時に別名でコピーします。 |
| clean  | 既存の space を削除して上書きします。 |

---

### drop-last — 最新スナップショットを削除する
```text
rinne drop-last [space] [--yes]
```
指定した space の最新スナップショットを ZIP／メタ情報のペアで削除します。  
`--yes` オプションを付けると確認なしで実行されます。  

---

### tidy — 古い履歴を整理する
```text
rinne tidy [space|--all] <keepCount>
```
古い履歴を削除し、チェーンハッシュ（prev/this）を再計算して整合性を保ちます。  
対象 space の最新 `<keepCount>` 件のみを残し、それ以前の履歴を削除します。  
全 space を対象とする場合は `--all` を指定します。

---

### log-output — ログ出力制御
```text
rinne log-output <on|off|clean>
```
ログファイル出力のON/OFFまたはクリアを切り替えます。

---

## 使用例

```text
rinne init
rinne save -m "初回スナップショット"
rinne space create feature-x
rinne space select feature-x
# ... 作業 ...
rinne save -m "機能追加"
rinne recompose main feature-x 00000003_20251027T120000 dev-a 00000063_20251029T140000
rinne verify
rinne backup backups/
```

---

## 設計原則・特徴

### 1. フルスナップショット方式
Rinne は差分ではなく、ディレクトリ全体をアーカイブとして保存します。  
これにより、状態は常に自己完結しており、単独で復元が可能です。  

### 2. メタデータとハッシュチェーン
各スナップショットにはメタデータが付属し、ハッシュ値によって履歴との整合性を検証します。  

### 3. 履歴の独立管理
Rinne は履歴を独立した「space」で管理します。
各 space は分離され、作業や実験を安全に同時進行・統合できます。

### 4. 再構成
複数のスナップショットを優先順に統合し、必要な部分だけを他の space から取り込めます。

### 5. 有限履歴の設計
無制限の履歴保存を目的とせず、不要な履歴は統合・整理することを前提とします。
必要な情報だけを残し、空間を循環的に管理することを目的としています。

### 6. OS・ツール低依存
履歴はシンプルなフォルダとファイルで構成され、
OS 標準機能で閲覧・コピーなど操作可能です。
特定のアプリに依存せず、ファイルシステムだけで復元や再構成が可能です。

### 7. AIや他ツールとの親和性
各時点の完成状態をそのまま保持しているため、
AI や他ツール、サーバースクリプトから直接利用できます。
差分処理を挟まず、“当時のまま” の要約・検証・分析が可能です。

### 8. 制約と留意点
Rinne は依存の少ない単純構造ですが、
差分ではなくディレクトリ全体を保存するため、
差分型に比べて容量を多く消費し、保存にも時間がかかります。

---

## 動作環境

- .NET 8.0 以降  
- Windows 
- CLI 実行形式

---

## 導入方法

1. 任意のフォルダに実行ファイル一式を配置してください。  
2. そのフォルダを環境変数 `PATH` に追加すると、どのディレクトリからでも `rinne` コマンドを実行できます。  

---