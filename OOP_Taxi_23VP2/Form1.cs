using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Npgsql;
using OOP_Taxi_23VP2.Models;

namespace OOP_Taxi_23VP2
{
    public partial class Form1 : Form
    {
        private const string _postgresConn = $"Host=localhost;Port=5432;Username=postgres;Password=postgres;Database={dbName}";
        private const string _removeConn = $"Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";
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

        private static async Task<bool> DatabaseExists(NpgsqlConnection conn)
        {
            using var check = new NpgsqlCommand("select 1 from pg_database where datname = @name", conn);
            check.Parameters.AddWithValue("name", dbName);
            return await check.ExecuteScalarAsync() != null;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            var hostBackupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), backupFileName);
            var containerBackupPath = $"/tmp/{backupFileName}";

            try
            {
                var dump = new ProcessStartInfo("docker",
                    $"exec {containerName} pg_dump -U {dbUser} -d {dbName} -f {containerBackupPath}")
                {
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var procDump = Process.Start(dump);
                procDump.WaitForExit();
                var errorDump = procDump.StandardError.ReadToEnd();

                if (procDump.ExitCode != 0)
                {
                    MessageBox.Show($"Ошибка при создании дампа:\n{errorDump}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                var copy = new ProcessStartInfo("docker",
                    $"cp {containerName}:{containerBackupPath} \"{hostBackupPath}\"")
                {
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var procCopy = Process.Start(copy);
                procCopy.WaitForExit();
                var errorCopy = procCopy.StandardError.ReadToEnd();

                if (procCopy.ExitCode != 0)
                {
                    MessageBox.Show($"Ошибка при копировании дампа:\n{errorCopy}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                MessageBox.Show($"Дамп успешно создан:\n{hostBackupPath}", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Исключение:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

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
        }

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
        }

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
        }

        private void button11_Click(object sender, EventArgs e)
        {
           LoadOrdersFromDatabase();
        }
    }
}
