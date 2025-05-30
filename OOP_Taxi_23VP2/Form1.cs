using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Npgsql;
using OOP_Taxi_23VP2.Models;

namespace OOP_Taxi_23VP2
{
    public partial class Form1 : Form
    {
        private const string _postgresConn = $"Host=localhost;Port=5432;Username=postgres;Password=postgres;Database={dbName};Pooling=false;Timeout=300;CommandTimeout=300";
        private const string _removeConn = $"Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres;Pooling=false;Timeout=300;CommandTimeout=300";
        private const string dbName = "taxi_orders";
        const string containerName = "postgres-db";
        const string dbUser = "postgres";
        const string backupFileName = $"postgres_dump.sql";


        public Form1()
        {
            InitializeComponent();
            ConfigureDataGridView();
            LoadOrdersFromDatabase();
        }

        private void ConfigureDataGridView()
        {
            dataGridView1.Columns.Clear();
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dataGridView1.AllowUserToResizeColumns = true;
        }

        /// <summary>
        ///  Создание БД
        /// </summary>
        private async void button2_Click(object sender, EventArgs e)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_removeConn);
                await conn.OpenAsync();

                if (await DatabaseExists(conn))
                {
                    MessageBox.Show($"База «{dbName}» уже существует.");
                    return;
                }

                using var cmd = new NpgsqlCommand($"create database \"{dbName}\"", conn);
                await cmd.ExecuteNonQueryAsync();

                await using var taxiConn = new NpgsqlConnection(_postgresConn);
                await taxiConn.OpenAsync();

                using var query = new NpgsqlCommand($"create table if not exists taxi (id serial PRIMARY KEY, driver_name varchar(255), car_number varchar(255), client_phone varchar(14), order_status varchar(10))", taxiConn);
                await query.ExecuteNonQueryAsync();

                await taxiConn.CloseAsync();
                await conn.CloseAsync();

                dataGridView1.DataSource = new List<Order>();
                MessageBox.Show($"База «{dbName}» успешно создана!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания: {ex.Message}");
            }
        }

        /// <summary>
        /// Удаление БД 
        /// </summary>
        private async void button3_Click(object sender, EventArgs e)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_removeConn);
                await conn.OpenAsync();

                if (!await DatabaseExists(conn))
                {
                    MessageBox.Show($"Базы «{dbName}» не найдено.");
                    return;
                }

                using (var terminate = new NpgsqlCommand(@"
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = @name
                    AND pid <> pg_backend_pid();", conn))
                {
                    terminate.Parameters.AddWithValue("name", dbName);
                    await terminate.ExecuteNonQueryAsync();
                }

                using (var drop = new NpgsqlCommand($"drop database \"{dbName}\"", conn))
                    await drop.ExecuteNonQueryAsync();
                await conn.CloseAsync();
                MessageBox.Show($"База «{dbName}» удалена.");
                dataGridView1.DataSource = new List<Order>();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления: {ex.Message}");
            }
        }


        /// <summary>
        ///  Проверка существования БД
        /// </summary>
        private static async Task<bool> DatabaseExists(NpgsqlConnection conn)
        {
            using var check = new NpgsqlCommand("select 1 from pg_database where datname = @name", conn);
            check.Parameters.AddWithValue("name", dbName);
            return await check.ExecuteScalarAsync() != null;
        }

        /// <summary>
        ///  Сохранение БД в файл
        /// </summary> 
        private void button4_Click(object sender, EventArgs e)
        {
            string pdfPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "taxi_export.pdf");

            try
            {
                var dataTable = new DataTable();
                using (var conn = new NpgsqlConnection(_postgresConn))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand("SELECT * FROM taxi ORDER BY id", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        dataTable.Load(reader);
                    }
                }

                string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
                var baseFont = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                var titleFont = new Font(baseFont, 14);
                var tableFont = new Font(baseFont, 10);

                using (var fs = new FileStream(pdfPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var doc = new Document(PageSize.A4, 25, 25, 30, 30))
                using (var writer = PdfWriter.GetInstance(doc, fs))
                {
                    doc.Open();

                    doc.Add(new Paragraph("Экспорт данных из таблицы 'taxi'", titleFont));
                    doc.Add(new Paragraph($"Дата экспорта: {DateTime.Now}\n\n", tableFont));

                    PdfPTable pdfTable = new PdfPTable(dataTable.Columns.Count);
                    pdfTable.WidthPercentage = 100;

                    foreach (DataColumn column in dataTable.Columns)
                    {
                        var cell = new PdfPCell(new Phrase(column.ColumnName, tableFont));
                        cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        pdfTable.AddCell(cell);
                    }

                    foreach (DataRow row in dataTable.Rows)
                    {
                        foreach (var item in row.ItemArray)
                        {
                            pdfTable.AddCell(new Phrase(item?.ToString(), tableFont));
                        }
                    }

                    doc.Add(pdfTable);
                    doc.Close();
                }

                MessageBox.Show($"PDF успешно создан:\n{pdfPath}", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при экспорте PDF:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///  Загрузка данных из БД в DataGridView
        /// </summary> 
        private async void LoadOrdersFromDatabase()
        {
            var query = "SELECT id, driver_name, car_number, client_phone, order_status FROM taxi";
            var info = new List<Order>();

            try
            {
                await using var conn = new NpgsqlConnection(_postgresConn);
                await conn.OpenAsync();

                using var cmd = new NpgsqlCommand(query, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    info.Add(new Order
                    {
                        Id = reader.GetInt32(0),
                        DriverName = reader.GetString(1),
                        CarNumber = reader.GetString(2),
                        ClientPhone = reader.GetString(3),
                        OrderStatus = reader.GetString(4)
                    });
                }

                dataGridView1.DataSource = info;
            }
            catch (PostgresException ex) when (ex.SqlState == "3D000" || ex.SqlState == "42P01")
            {
            }
            catch
            {
            }
        }

        /// <summary>
        ///  Поиск по номеру машины
        /// </summary>
        private void button1_Click(object sender, EventArgs e)
        {
            using var conn = new NpgsqlConnection(_postgresConn);
            conn.Open();
            var carNumber = textBox5.Text;
            var query = "select * from taxi where car_number = @carNumber";
            var info = new List<Order>();
            using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@carNumber", carNumber);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                info.Add(new Order
                {
                    Id = reader.GetInt32(0),
                    DriverName = reader.GetString(1),
                    CarNumber = reader.GetString(2),
                    ClientPhone = reader.GetString(3),
                    OrderStatus = reader.GetString(4)
                });
            }

            conn.Close();
            dataGridView1.DataSource = info;
        }

        /// <summary>
        ///  Добавление записи
        /// </summary> 
        private async void button8_Click(object sender, EventArgs e)
        {
            var newInfo = new Order
            {
                DriverName = textBox1.Text,
                CarNumber = textBox2.Text,
                ClientPhone = textBox3.Text,
                OrderStatus = textBox4.Text
            };

            await using var conn = new NpgsqlConnection(_postgresConn);
            await conn.OpenAsync();
            var query = "insert into taxi(driver_name, car_number, client_phone, order_status) values (@driverName, @carNumber, @clientPhone, @orderStatus)";
            try
            {
                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@driverName", newInfo.DriverName);
                cmd.Parameters.AddWithValue("@carNumber", newInfo.CarNumber);
                cmd.Parameters.AddWithValue("@clientPhone", newInfo.ClientPhone);
                cmd.Parameters.AddWithValue("@orderStatus", newInfo.OrderStatus);
                cmd.ExecuteNonQuery();
                LoadOrdersFromDatabase();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при вставке данных: {ex.Message}");
            }
        }

        /// <summary>
        ///  Обновление записи
        /// </summary>
        private async void button9_Click(object sender, EventArgs e)
        {
            var updatedInfo = new Order
            {
                Id = int.Parse(textBox10.Text),
                DriverName = textBox6.Text,
                CarNumber = textBox7.Text,
                ClientPhone = textBox9.Text,
                OrderStatus = textBox8.Text
            };

            var query = @"update taxi
            set driver_name = @driverName,
            car_number = @carNumber,
            client_phone = @clientPhone,
            order_status = @orderStatus
            where id = @id";

            try
            {
                await using var conn = new NpgsqlConnection(_postgresConn);
                await conn.OpenAsync();

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@driverName", updatedInfo.DriverName);
                cmd.Parameters.AddWithValue("@carNumber", updatedInfo.CarNumber);
                cmd.Parameters.AddWithValue("@clientPhone", updatedInfo.ClientPhone);
                cmd.Parameters.AddWithValue("@orderStatus", updatedInfo.OrderStatus);
                cmd.Parameters.AddWithValue("@id", updatedInfo.Id);

                await cmd.ExecuteNonQueryAsync();

                LoadOrdersFromDatabase();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обновлении: {ex.Message}");
            }
        }

        /// <summary>
        ///  Удаление записи
        /// </summary>
        private async void button7_Click(object sender, EventArgs e)
        {
            var id = int.Parse(numericUpDown1.Text);
            try
            {
                await using var conn = new NpgsqlConnection(_postgresConn);
                await conn.OpenAsync();

                var query = @"delete from taxi where id = @id";
                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", id);

                await cmd.ExecuteNonQueryAsync();

                LoadOrdersFromDatabase();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при удалении: {ex.Message}");
            }
        }

        /// <summary>
        ///  Фильтр "Завершен"
        /// </summary>
        private void button5_Click(object sender, EventArgs e)
        {
            using var conn = new NpgsqlConnection(_postgresConn);
            conn.Open();
            var query = $"select * from taxi where order_status = @orderStatus";
            var info = new List<Order>();
            using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@orderStatus", "Завершен");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                info.Add(new Order
                {
                    Id = reader.GetInt32(0),
                    DriverName = reader.GetString(1),
                    CarNumber = reader.GetString(2),
                    ClientPhone = reader.GetString(3),
                    OrderStatus = reader.GetString(4)
                });
            }

            conn.Close();
            dataGridView1.DataSource = info.ToList();
            textBox5.Text = $"Записей со статусом 'Завершен': {info.Count} ";
        }

        /// <summary>
        ///  Фильтр "В работе"
        /// </summary>
        private void button6_Click(object sender, EventArgs e)
        {
            using var conn = new NpgsqlConnection(_postgresConn);
            conn.Open();
            var query = $"select * from taxi where order_status = @orderStatus";
            var info = new List<Order>();
            using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@orderStatus", "В работе");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                info.Add(new Order
                {
                    Id = reader.GetInt32(0),
                    DriverName = reader.GetString(1),
                    CarNumber = reader.GetString(2),
                    ClientPhone = reader.GetString(3),
                    OrderStatus = reader.GetString(4)
                });
            }

            conn.Close();
            dataGridView1.DataSource = info.ToList();
            textBox5.Text = $"Записей со статусом 'В работе': {info.Count} ";
        }

        /// <summary>
        ///  Фильтр "Ожидание"
        /// </summary>
        private void button10_Click(object sender, EventArgs e)
        {
            using var conn = new NpgsqlConnection(_postgresConn);
            conn.Open();
            var query = $"select * from taxi where order_status = @orderStatus";
            var info = new List<Order>();
            using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@orderStatus", "Ожидание");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                info.Add(new Order
                {
                    Id = reader.GetInt32(0),
                    DriverName = reader.GetString(1),
                    CarNumber = reader.GetString(2),
                    ClientPhone = reader.GetString(3),
                    OrderStatus = reader.GetString(4)
                });
            }

            conn.Close();
            dataGridView1.DataSource = info.ToList();
            textBox5.Text = $"Записей со статусом 'Ожидание': {info.Count} ";
        }

        /// <summary>
        ///  Сброс фильтров
        /// </summary>
        private void button11_Click(object sender, EventArgs e)
        {
            LoadOrdersFromDatabase();
            textBox5.Clear();
        }

        /// <summary>
        ///  Сортировка по возрастанию ID
        /// </summary>
        private async void button12_Click(object sender, EventArgs e)
        {
            var query = "SELECT id, driver_name, car_number, client_phone, order_status FROM taxi ORDER BY id ASC";
            var info = new List<Order>();

            try
            {
                await using var conn = new NpgsqlConnection(_postgresConn);
                await conn.OpenAsync();

                using var cmd = new NpgsqlCommand(query, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    info.Add(new Order
                    {
                        Id = reader.GetInt32(0),
                        DriverName = reader.GetString(1),
                        CarNumber = reader.GetString(2),
                        ClientPhone = reader.GetString(3),
                        OrderStatus = reader.GetString(4)
                    });
                }

                dataGridView1.DataSource = info;
            }
            catch (PostgresException ex) when (ex.SqlState == "3D000" || ex.SqlState == "42P01")
            {
            }
            catch
            {
            }
        }

        /// <summary>
        ///  Сортировка по убыванию ID
        /// </summary> 
        private async void button13_Click(object sender, EventArgs e)
        {
            var query = "SELECT id, driver_name, car_number, client_phone, order_status FROM taxi ORDER BY id DESC";
            var info = new List<Order>();

            try
            {
                await using var conn = new NpgsqlConnection(_postgresConn);
                await conn.OpenAsync();

                using var cmd = new NpgsqlCommand(query, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    info.Add(new Order
                    {
                        Id = reader.GetInt32(0),
                        DriverName = reader.GetString(1),
                        CarNumber = reader.GetString(2),
                        ClientPhone = reader.GetString(3),
                        OrderStatus = reader.GetString(4)
                    });
                }

                dataGridView1.DataSource = info;
            }
            catch (PostgresException ex) when (ex.SqlState == "3D000" || ex.SqlState == "42P01")
            {
            }
            catch
            {
            }
        }
    }
}
